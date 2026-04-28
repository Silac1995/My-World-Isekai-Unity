using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class CharacterActions : CharacterSystem
{
    public Action<CharacterAction> OnActionStarted;
    public Action OnActionFinished;
    private float _actionStartTime; // Pour calculer la progression

    private CharacterAction _currentAction;
    private Coroutine _actionRoutine; // Référence pour éviter les accumulations

    public CharacterAction CurrentAction => _currentAction;

    public float GetActionProgress()
    {
        if (_currentAction == null || _currentAction.Duration <= 0) return 0f;
        float elapsed = Time.time - _actionStartTime;
        return Mathf.Clamp01(elapsed / _currentAction.Duration);
    }

    public bool ExecuteAction(CharacterAction action)
    {
        if (action == null || _currentAction != null) return false;
        if (!action.CanExecute()) return false;

        // Owner/Client -> Server Intent (Only if it's the real action, not a proxy)
        if (!IsServer && IsOwner && !(action is CharacterVisualProxyAction) && !action.IsReplicatedInternally)
        {
            // Ask the server to broadcast visuals to everyone else
            NotifyActionStartedServerRpc(action.ShouldPlayGenericActionAnimation, action.Duration, action.ActionName);
        }

        if (IsServer && !(action is CharacterVisualProxyAction) && !action.IsReplicatedInternally)
        {
            // Server broadcasts the visual proxy to all clients
            // MODIFICATION: Add ActionName to sync UI display
            BroadcastActionVisualsClientRpc(action.ShouldPlayGenericActionAnimation, action.Duration, action.ActionName);
        }

        _currentAction = action;
        _actionStartTime = Time.time;
        _currentAction.OnActionFinished += CleanupAction;

        OnActionStarted?.Invoke(_currentAction);

        // 1. On lance l'initialisation de l'action
        _currentAction.OnStart();

        // 2. GESTION DU FLUX (Instantané vs Temporisé)
        if (action.Duration <= 0)
        {
            try
            {
                if (IsServer || action is CharacterVisualProxyAction)
                    action.OnApplyEffect(); 
                else 
                    action.OnApplyEffect(); 
                
                action.Finish();
            }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterActions] Erreur Action Instantanée: {e.Message}");
                CleanupAction();
            }
        }
        else
        {
            _actionRoutine = StartCoroutine(ActionTimerRoutine(_currentAction));
        }

        return true;
    }

    [Rpc(SendTo.Server)]
    private void NotifyActionStartedServerRpc(bool shouldPlayGeneric, float duration, string actionName)
    {
        // Server relays the visual proxy to all clients (excluding the owner who already predicted it)
        BroadcastActionVisualsClientRpc(shouldPlayGeneric, duration, actionName);
    }

    [Rpc(SendTo.Server)]
    public void RequestCraftServerRpc(string itemId, Color primaryColor, Color secondaryColor, Vector3 stationPosition)
    {
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO itemSO = System.Array.Find(allItems, match => match.ItemId == itemId);
        if (itemSO == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find ItemSO with ID '{itemId}'");
            return;
        }

        CraftingStation station = FindCraftingStationNear(stationPosition);
        if (station == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find CraftingStation near {stationPosition}");
            return;
        }

        station.Craft(itemSO, _character, primaryColor, secondaryColor);
    }

    // ────────────────────── Generic RPCs ──────────────────────

    /// <summary>
    /// Sent by the server to the owning client when a non-wearable WorldItem is picked up.
    /// The client reconstructs the ItemInstance from network data and adds it to
    /// inventory/hands locally. Wearables go through CharacterEquipAction on the server
    /// instead (they sync via NetworkList).
    /// </summary>
    [Rpc(SendTo.Owner)]
    public void ReceiveItemPickupClientRpc(NetworkItemData itemData)
    {
        if (itemData.ItemId.IsEmpty) return;

        string id = itemData.ItemId.ToString();
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO so = System.Array.Find(allItems, match => match.ItemId == id);
        if (so == null)
        {
            Debug.LogError($"<color=red>[CharacterActions]</color> Client could not find ItemSO '{id}' for pickup.");
            return;
        }

        ItemInstance instance = so.CreateInstance();
        JsonUtility.FromJsonOverwrite(itemData.JsonData.ToString(), instance);
        instance.ItemSO = so;

        if (_character.CharacterEquipment != null)
            _character.CharacterEquipment.PickUpItem(instance);

        Debug.Log($"<color=green>[CharacterActions]</color> Client received item pickup: {so.ItemName}");
    }

    /// <summary>
    /// Generic server-side despawn for any NetworkObject.
    /// Used by CharacterPickUpItem and other actions that need to remove
    /// a networked object from a client context.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestDespawnServerRpc(NetworkObjectReference targetRef)
    {
        if (!targetRef.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[CharacterActions] Server: Could not resolve NetworkObject for despawn.");
            return;
        }

        if (netObj.IsSpawned)
            netObj.Despawn(true);
    }

    /// <summary>
    /// Client requests the server to run the harvest effect: run Harvest() on the target,
    /// spawn the resulting WorldItem, and register a pickup task for NPC workers.
    /// The target Harvestable is resolved by world position (Harvestable is a scene-authored
    /// MonoBehaviour, so its position is identical on server and client).
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestHarvestServerRpc(Vector3 harvestablePosition)
    {
        Harvestable target = FindHarvestableNear(harvestablePosition);
        if (target == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find Harvestable near {harvestablePosition}");
            return;
        }

        ApplyHarvestOnServer(target);
    }

    /// <summary>
    /// Server-side (or offline) harvest execution. Runs the Harvest, spawns the WorldItem,
    /// and registers a pickup task for NPC workers. Called from:
    ///   - RequestHarvestServerRpc (networked client-owner path)
    ///   - CharacterHarvestAction.OnApplyEffect on the server (NPCs / host)
    ///   - CharacterHarvestAction.OnApplyEffect in offline mode (NetworkObject not spawned)
    /// Blocks only the networked-client case: IsSpawned && !IsServer.
    /// </summary>
    public ItemSO ApplyHarvestOnServer(Harvestable target)
    {
        if (IsSpawned && !IsServer) return null;
        if (target == null || !target.CanHarvest()) return null;
        if (_character == null) return null;

        ItemSO harvestedItem = target.Harvest(_character);
        if (harvestedItem == null) return null;

        Vector3 spawnPos = _character.transform.position + _character.transform.forward * 0.5f + Vector3.up * 0.3f;
        WorldItem spawnedItem = WorldItem.SpawnWorldItem(harvestedItem, spawnPos);

        if (spawnedItem != null && _character.CharacterJob != null)
        {
            var workAssignment = _character.CharacterJob.ActiveJobs.FirstOrDefault(j => j.AssignedJob is JobHarvester);
            if (workAssignment != null && workAssignment.Workplace != null)
            {
                workAssignment.Workplace.TaskManager?.RegisterTask(new PickupLooseItemTask(spawnedItem));
            }
        }

        return harvestedItem;
    }

    private Harvestable FindHarvestableNear(Vector3 position, float maxDistance = 2.5f)
    {
        Harvestable[] all = UnityEngine.Object.FindObjectsByType<Harvestable>(FindObjectsSortMode.None);
        Harvestable best = null;
        float bestDist = maxDistance;
        foreach (var h in all)
        {
            float dist = Vector3.Distance(h.transform.position, position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = h;
            }
        }
        return best;
    }

    /// <summary>
    /// Client requests the server to spawn a dropped item in the world.
    /// Clients cannot spawn NetworkObjects — they send the item data to the server.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestItemDropServerRpc(string itemId, string jsonData, Vector3 ownerPosition)
    {
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO so = System.Array.Find(allItems, match => match.ItemId == itemId);
        if (so == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find ItemSO '{itemId}' for drop.");
            return;
        }

        ItemInstance instance = so.CreateInstance();
        JsonUtility.FromJsonOverwrite(jsonData, instance);
        instance.ItemSO = so;

        Vector3 dropPos = ownerPosition + Vector3.up * 1.5f;
        Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0, UnityEngine.Random.Range(-0.3f, 0.3f));
        WorldItem.SpawnWorldItem(instance, dropPos + offset);

        Debug.Log($"<color=green>[CharacterActions]</color> Server spawned dropped item: {so.ItemName}");
    }

    // ────────────────────── Furniture Placement RPCs ──────────────────────

    [Rpc(SendTo.Server)]
    public void RequestFurniturePlaceServerRpc(string furnitureItemSOId, Vector3 visualPosition, Vector3 gridAnchor, Quaternion rotation)
    {
        // Resolve the FurnitureItemSO
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        FurnitureItemSO furnitureItemSO = null;
        foreach (var item in allItems)
        {
            if (item is FurnitureItemSO fso && fso.ItemId == furnitureItemSOId)
            {
                furnitureItemSO = fso;
                break;
            }
        }

        if (furnitureItemSO == null || furnitureItemSO.InstalledFurniturePrefab == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find FurnitureItemSO '{furnitureItemSOId}'");
            return;
        }

        Furniture placed = Instantiate(furnitureItemSO.InstalledFurniturePrefab, visualPosition, rotation);

        var netObj = placed.GetComponent<NetworkObject>();
        if (netObj != null)
            netObj.Spawn();

        // Register with room grid if inside a room
        Room room = FindRoomAtPosition(gridAnchor);
        if (room != null && room.FurnitureManager != null)
        {
            room.FurnitureManager.RegisterSpawnedFurniture(placed, gridAnchor);
        }

        Debug.Log($"<color=green>[CharacterActions]</color> Server placed {furnitureItemSO.name} at {visualPosition} (anchor: {gridAnchor}).");
    }

    [Rpc(SendTo.Server)]
    public void RequestFurniturePickUpServerRpc(NetworkObjectReference furnitureRef)
    {
        if (!furnitureRef.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[CharacterActions] Server: Could not resolve furniture NetworkObject for pickup.");
            return;
        }

        Furniture furniture = netObj.GetComponent<Furniture>();
        if (furniture == null) return;

        // Unregister from room grid
        Room parentRoom = furniture.GetComponentInParent<Room>();
        if (parentRoom != null && parentRoom.FurnitureManager != null)
        {
            parentRoom.FurnitureManager.UnregisterAndRemove(furniture);
        }

        // Despawn
        if (netObj.IsSpawned)
            netObj.Despawn(true);

        Debug.Log($"<color=green>[CharacterActions]</color> Server despawned furniture '{furniture.FurnitureName}'.");
    }

    /// <summary>
    /// Client → Server: enqueue CharacterAction_SleepOnFurniture for the local
    /// player Character. Server resolves the bed by NetworkObjectReference
    /// (the parent building's NetworkObject — beds have no NO of their own per
    /// the no-nested-NO rule), validates the slot, sets PendingSkipHours, and
    /// queues the action. The auto-trigger watcher in TimeSkipController will
    /// then fire RequestSkip once all connected players are sleeping with
    /// PendingSkipHours > 0.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestSleepOnFurnitureServerRpc(NetworkObjectReference parentRef, int slotIndex, int desiredHours)
    {
        if (!parentRef.TryGet(out NetworkObject parentNetObj))
        {
            Debug.LogWarning("[CharacterActions] RequestSleepOnFurnitureServerRpc: parentRef did not resolve to a NetworkObject.");
            return;
        }

        BedFurniture bed = parentNetObj.GetComponentInChildren<BedFurniture>();
        if (bed == null)
        {
            Debug.LogWarning("[CharacterActions] RequestSleepOnFurnitureServerRpc: no BedFurniture found under the resolved NetworkObject.");
            return;
        }

        if (slotIndex < 0 || slotIndex >= bed.SlotCount)
        {
            Debug.LogWarning($"[CharacterActions] RequestSleepOnFurnitureServerRpc: slotIndex {slotIndex} out of range for {bed.FurnitureName}.");
            return;
        }

        // Set the per-skip target so the auto-trigger watcher can fire.
        if (desiredHours > 0)
        {
            _character.SetPendingSkipHours(desiredHours);
        }

        var action = new CharacterAction_SleepOnFurniture(_character, bed, slotIndex);
        if (!ExecuteAction(action))
        {
            Debug.LogWarning($"[CharacterActions] RequestSleepOnFurnitureServerRpc: ExecuteAction rejected for {_character.CharacterName} on {bed.FurnitureName}.");
        }
    }

    private static Room FindRoomAtPosition(Vector3 position)
    {
        Room[] allRooms = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsSortMode.None);
        foreach (var room in allRooms)
        {
            if (room.IsPointInsideRoom(position)) return room;
        }
        return null;
    }

    private CraftingStation FindCraftingStationNear(Vector3 position, float maxDistance = 2f)
    {
        CraftingStation[] stations = UnityEngine.Object.FindObjectsOfType<CraftingStation>();
        CraftingStation best = null;
        float bestDist = maxDistance;
        foreach (var s in stations)
        {
            float dist = Vector3.Distance(s.transform.position, position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = s;
            }
        }
        return best;
    }

    [Rpc(SendTo.NotServer)]
    private void BroadcastActionVisualsClientRpc(bool shouldPlayGeneric, float duration, string actionName)
    {
        if (IsOwner && _currentAction != null) return; // Owner may have already predicted it

        ClearCurrentAction(); // Clear any visual desyncs

        var proxy = new CharacterVisualProxyAction(_character, duration, shouldPlayGeneric, actionName);
        ExecuteAction(proxy);
    }

    private IEnumerator ActionTimerRoutine(CharacterAction action)
    {
        if (action == null) yield break;

        if (action.Duration > 0)
        {
            yield return new WaitForSeconds(action.Duration);
        }

        if (_currentAction != action) yield break;

        try
        {
            action.OnApplyEffect();
            action.Finish();
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterActions] Erreur durant l'exécution de l'action: {e.Message}");
            CleanupAction();
        }
    }

    private void CleanupAction()
    {
        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction;
        }

        _currentAction = null;
        _actionRoutine = null;

        OnActionFinished?.Invoke();
    }

    public void ClearCurrentAction()
    {
        if (IsServer) CancelActionVisualsClientRpc();
        ClearCurrentActionLocally();
    }

    [Rpc(SendTo.NotServer)]
    private void CancelActionVisualsClientRpc()
    {
        ClearCurrentActionLocally();
    }

    private void ClearCurrentActionLocally()
    {
        if (_actionRoutine != null)
        {
            StopCoroutine(_actionRoutine);
            _actionRoutine = null;
        }

        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction;
            _currentAction.OnCancel(); 

            var animHandler = _character.CharacterVisual?.CharacterAnimator;
            if (animHandler != null)
            {
                animHandler.ResetActionTriggers();
            }

            OnActionFinished?.Invoke();
        }

        _currentAction = null;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        StopAllCoroutines();
        _currentAction = null;
        _actionRoutine = null;
    }

    protected override void HandleIncapacitated(Character character)
    {
        ClearCurrentActionLocally();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (inCombat) ClearCurrentActionLocally();
    }
}

public class CharacterVisualProxyAction : CharacterAction
{
    private bool _shouldPlayGeneric;
    private string _proxyActionName;
    public override bool ShouldPlayGenericActionAnimation => _shouldPlayGeneric;
    public override string ActionName => _proxyActionName;

    public CharacterVisualProxyAction(Character character, float duration, bool shouldPlayGeneric, string actionName) : base(character, duration)
    {
        _shouldPlayGeneric = shouldPlayGeneric;
        _proxyActionName = actionName;
    }

    public override void OnStart() { }
    
    public override void OnApplyEffect() 
    { 
        // Visual proxy does not mutate any game state
    } 
}
