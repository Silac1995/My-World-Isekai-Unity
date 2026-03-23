using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using MWI.Time;
using System.Linq;

namespace MWI.WorldSystem
{
    [RequireComponent(typeof(BoxCollider))]
    public class MapController : NetworkBehaviour
    {
        [Header("Map Settings")]
        [Tooltip("Unique ID matching the Decoupled Character Save (e.g. World_Aethelgard_Region_North)")]
        public string MapId;
        
        [Tooltip("Use this to determine if the map is visually loaded or offset")]
        public bool IsInteriorOffset = false;

        [Header("Runtime State")]
        public int ConnectedPlayersCount = 0;
        public bool IsHibernating = false;
        [SerializeField] private int _activePlayerCount = 0;

        private BoxCollider _mapTrigger;
        private HashSet<ulong> _activePlayers = new HashSet<ulong>();
        private MapSaveData _hibernationData;

        // Dependencies
        private TimeManager _timeManager => TimeManager.Instance;

        private void Awake()
        {
            _mapTrigger = GetComponent<BoxCollider>();
            _mapTrigger.isTrigger = true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsServer) return;

            // Subscribe to NetworkManager disconnects to ensure _activePlayers doesn't drift
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;

            // Optional: Delay the initial check slightly so players have time to spawn
            Invoke(nameof(CheckHibernationState), 1f);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            if (!IsServer) return;
            
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
            }
        }

        #region Player Tracking

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;

            // Only track actual PlayerObjects, not NPCs or random physics objects
            if (other.CompareTag("Character") && other.TryGetComponent(out Character character))
            {
                if (character.NetworkObject != null && character.NetworkObject.IsPlayerObject)
                {
                    AddPlayer(character.OwnerClientId);
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;

            if (other.CompareTag("Character") && other.TryGetComponent(out Character character))
            {
                if (character.NetworkObject != null && character.NetworkObject.IsPlayerObject)
                {
                    RemovePlayer(character.OwnerClientId);
                }
            }
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (!IsServer) return;

            // If a client disconnects unexpectedly, remove them from this map 
            // even if OnTriggerExit never fired.
            RemovePlayer(clientId);
        }

        private void AddPlayer(ulong clientId)
        {
            if (_activePlayers.Add(clientId))
            {
                _activePlayerCount = _activePlayers.Count;
                Debug.Log($"<color=cyan>[MapController]</color> Player {clientId} entered '{MapId}'. Total: {_activePlayerCount}");
                CheckWakeUp();
            }
        }

        private void RemovePlayer(ulong clientId)
        {
            if (_activePlayers.Remove(clientId))
            {
                _activePlayerCount = _activePlayers.Count;
                Debug.Log($"<color=cyan>[MapController]</color> Player {clientId} left '{MapId}'. Total: {_activePlayerCount}");
                CheckHibernationState();
            }
        }

        #endregion

        #region Hibernation Logic

        private void CheckHibernationState()
        {
            if (_activePlayers.Count == 0 && !IsHibernating)
            {
                Hibernate();
            }
        }

        private void CheckWakeUp()
        {
            if (_activePlayers.Count > 0 && IsHibernating)
            {
                WakeUp();
            }
        }

        private double GetAbsoluteTimeInDays()
        {
            if (_timeManager == null) return 0;
            // Absolute time formula: (Full Days Passed) + (Fraction of Current Day)
            return _timeManager.CurrentDay + _timeManager.CurrentTime01;
        }

        private void Hibernate()
        {
            if (IsHibernating) return;

            IsHibernating = true;
            Debug.Log($"<color=orange>[MapController]</color> Map '{MapId}' entering Hibernation (0 players).");

            _hibernationData = new MapSaveData()
            {
                MapId = this.MapId,
                LastHibernationTime = GetAbsoluteTimeInDays()
            };

            // 1. Find all NPCs inside this Map
            // Using OverlapBox is safer than tracking every single NPC manually, 
            // ensuring we grab any NPC physically standing in the map boundaries right now.
            Collider[] colliders = Physics.OverlapBox(_mapTrigger.bounds.center, _mapTrigger.bounds.extents, Quaternion.identity);
            
            foreach (var col in colliders)
            {
                if (col.CompareTag("Character") && col.TryGetComponent(out Character npc))
                {
                    // Ignore Players
                    if (npc.NetworkObject != null && npc.NetworkObject.IsPlayerObject) continue;

                    // Serialize to V1 Dumb Data
                    HibernatedNPCData npcData = new HibernatedNPCData()
                    {
                        CharacterId = npc.CharacterName, // Assuming Name is unique for now, will replace with True GUID later
                        PrefabName = npc.gameObject.name.Replace("(Clone)", "").Trim(),
                        Position = npc.transform.position,
                        Rotation = npc.transform.rotation
                    };

                    _hibernationData.HibernatedNPCs.Add(npcData);

                    // 2. Despawn & Destroy the physical instance
                    if (npc.NetworkObject != null && npc.NetworkObject.IsSpawned)
                    {
                        npc.NetworkObject.Despawn(true); // true = destroy the GameObject
                    }
                    else
                    {
                        Destroy(npc.gameObject);
                    }
                }
            }

            Debug.Log($"<color=orange>[MapController]</color> Map '{MapId}' Hibernated. {_hibernationData.HibernatedNPCs.Count} NPCs serialized and despawned.");
        }

        private void WakeUp()
        {
            if (!IsHibernating) return;

            IsHibernating = false;
            
            double currentTime = GetAbsoluteTimeInDays();
            double deltaDays = currentTime - (_hibernationData?.LastHibernationTime ?? currentTime);

            Debug.Log($"<color=green>[MapController]</color> Map '{MapId}' Waking Up! Simulating {deltaDays:F2} offline days.");

            if (_hibernationData != null && _hibernationData.HibernatedNPCs.Count > 0)
            {
                // V1 Simulation: Use external MacroSimulator to process elapsed time
                MacroSimulator.SimulateCatchUp(_hibernationData, _timeManager.CurrentDay, _timeManager.CurrentTime01);

                // Respawn
                foreach (var npcData in _hibernationData.HibernatedNPCs)
                {
                    // NOTE: Hardcoding 'Prefabs/' path for now, adjust based on project structure
                    GameObject prefab = Resources.Load<GameObject>($"Prefabs/{npcData.PrefabName}");
                    
                    if (prefab == null)
                    {
                        Debug.LogError($"[MapController] Could not find prefab {npcData.PrefabName} to wake up {npcData.CharacterId}!");
                        continue;
                    }

                    GameObject inst = Instantiate(prefab, npcData.Position, npcData.Rotation);
                    
                    if (inst.TryGetComponent(out NetworkObject netObj))
                    {
                        netObj.Spawn(true);
                    }
                }
            }

            _hibernationData = null; // Clear data so it doesn't leak memory while map is awake
        }

        // (RunMacroSimulation_V1 removed, see MacroSimulator.cs)

        #endregion
        
        /// <summary>
        /// Call this directly from Map Transition doors/portals to force a fast Add/Remove 
        /// before physics triggers have a chance to update.
        /// </summary>
        public void ForcePlayerTransition(ulong clientId, bool entering)
        {
            if (!IsServer) return;
            
            if (entering) AddPlayer(clientId);
            else RemovePlayer(clientId);
        }
    }
}
