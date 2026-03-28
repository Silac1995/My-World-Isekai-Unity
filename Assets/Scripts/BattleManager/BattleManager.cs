using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

public class BattleManager : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private List<BattleTeam> _teams = new List<BattleTeam>();
    [SerializeField] private BattleTeam _battleTeamInitiator;
    [SerializeField] private BattleTeam _battleTeamTarget;
    [SerializeField] private Collider _battleZone;
    [SerializeField] private Unity.AI.Navigation.NavMeshModifierVolume _battleZoneModifier;
    [SerializeField] private LineRenderer _battleZoneLine;
    [SerializeField] private ParticleSystem _battleZoneParticles;

    [Header("Zone Particle Overrides")]
    [Tooltip("Particles emitted per second along the zone border.")]
    [SerializeField] private float _particleRate = 25f;
    [Tooltip("Color tint applied to zone border particles.")]
    [SerializeField] private Color _particleColor = new Color(1.2f, 0.9f, 0.4f, 0.5f);
    [Tooltip("Min/max particle size.")]
    [SerializeField] private Vector2 _particleSize = new Vector2(0.05f, 0.15f);
    [Tooltip("Min/max particle lifetime in seconds.")]
    [SerializeField] private Vector2 _particleLifetime = new Vector2(2f, 4f);
    [Tooltip("Upward drift speed of particles.")]
    [SerializeField] private Vector2 _particleDriftY = new Vector2(0.1f, 0.3f);

    [Header("Zone Settings")]
    [SerializeField] private Vector3 _baseBattleZoneSize = new Vector3(25f, 35f, 10f);
    [SerializeField] private float _perParticipantGrowthRate = 0.3f;
    [SerializeField] private int _participantsPerTier = 6;

    [Header("Initiative System")]
    [SerializeField] private float _ticksPerSecond = 10f;
    private float _tickTimer = 0f;

    private bool _isBattleEnded = false;

    // --- NOUVEAUX CONTRÔLEURS DE DÉLÉGATION ---
    private BattleZoneController _zoneController;
    private CombatEngagementCoordinator _engagementCoordinator;

    // Liste pour le debug
    [SerializeField] private List<Character> _allParticipants = new List<Character>();

    [Header("Debug — Engagements actifs")]
    [SerializeField] private List<string> _debugEngagements = new List<string>();

    public List<BattleTeam> BattleTeams => _teams;
    public bool IsBattleEnded => _isBattleEnded;
    public CombatEngagementCoordinator Coordinator => _engagementCoordinator;

    public event Action<Character> OnParticipantAdded;

    public void Initialize(Character initiator, Character target)
    {
        if (!IsServer) return;

        if (initiator == null || target == null) return;

        // 1. Setup des équipes (On s'assure qu'il n'y en a QUE 2)
        _teams.Clear();
        _battleTeamInitiator = new BattleTeam();
        _battleTeamTarget = new BattleTeam();

        _battleTeamInitiator.AddCharacter(initiator);
        _battleTeamTarget.AddCharacter(target);

        _teams.Add(_battleTeamInitiator);
        _teams.Add(_battleTeamTarget);

        _zoneController = new BattleZoneController(this, _battleZoneModifier, _battleZoneLine, _battleZoneParticles, BuildParticleSettings(), _baseBattleZoneSize, _perParticipantGrowthRate, _participantsPerTier);
        _engagementCoordinator = new CombatEngagementCoordinator(this);

        // 2. Création physique de la zone
        _zoneController.CreateBattleZone(initiator, target);

        // 3. Inscription des participants
        RegisterParticipants();

        // 4. Créer l'engagement initial entre l'initiateur et la cible
        _engagementCoordinator.RequestEngagement(initiator, target);

        // 5. Rendu visuel UNIQUE (pas dans Update)
        _zoneController.DrawBattleZoneOutline();

        Debug.Log($"<color=orange>[Battle]</color> Combat lance : {initiator.name} vs {target.name}");

        var initNet = initiator.GetComponent<NetworkObject>();
        var targNet = target.GetComponent<NetworkObject>();
        if (initNet != null && targNet != null)
        {
            InitializeClientRpc(initNet, targNet);
        }
    }

    [ClientRpc]
    private void InitializeClientRpc(NetworkObjectReference initiatorRef, NetworkObjectReference targetRef)
    {
        if (IsServer) return;

        if (!initiatorRef.TryGet(out NetworkObject iNet) || !targetRef.TryGet(out NetworkObject tNet))
        {
            Debug.LogError($"<color=red>[Battle Client]</color> InitializeClientRpc: could not resolve NetworkObjects.");
            return;
        }

        Character initiator = iNet.GetComponent<Character>();
        Character target = tNet.GetComponent<Character>();

        // IDEMPOTENT: If coordinator was already lazily created by an earlier AddParticipantClientRpc,
        // just merge the original combatants into the existing teams. Don't clear everything.
        if (_engagementCoordinator != null && _teams.Count > 0)
        {
            Debug.Log($"<color=cyan>[Battle Client]</color> InitializeClientRpc (late merge): adding original {initiator?.CharacterName} + {target?.CharacterName}");
            if (initiator != null && GetTeamOf(initiator) == null)
            {
                _battleTeamInitiator.AddCharacter(initiator);
                RegisterCharacter(initiator);
            }
            if (target != null && GetTeamOf(target) == null)
            {
                _battleTeamTarget.AddCharacter(target);
                RegisterCharacter(target);
            }
            _engagementCoordinator.RequestEngagement(initiator, target);
            _zoneController?.CreateBattleZone(initiator, target);
            _zoneController?.DrawBattleZoneOutline();
            return;
        }

        // Normal first-time initialization on client
        _teams.Clear();
        _battleTeamInitiator = new BattleTeam();
        _battleTeamTarget = new BattleTeam();

        _battleTeamInitiator.AddCharacter(initiator);
        _battleTeamTarget.AddCharacter(target);

        _teams.Add(_battleTeamInitiator);
        _teams.Add(_battleTeamTarget);

        _zoneController = new BattleZoneController(this, _battleZoneModifier, _battleZoneLine, _battleZoneParticles, BuildParticleSettings(), _baseBattleZoneSize, _perParticipantGrowthRate, _participantsPerTier);
        _engagementCoordinator = new CombatEngagementCoordinator(this);

        _zoneController.CreateBattleZone(initiator, target);
        RegisterParticipants();
        _engagementCoordinator.RequestEngagement(initiator, target);
        _zoneController.DrawBattleZoneOutline();
        Debug.Log($"<color=cyan>[Battle Client]</color> InitializeClientRpc completed: {initiator?.CharacterName} vs {target?.CharacterName}");
    }

    private void Update()
    {
        _zoneController?.Tick();

        if (_isBattleEnded) return;

        // --- NOUVEAU : VERIFICATION DE FIN DE COMBAT EN CONTINU ---
        // Seul le serveur valide la fin du combat.
        if (IsServer)
        {
            if (_battleTeamInitiator.IsTeamEliminated() || _battleTeamTarget.IsTeamEliminated())
            {
                Debug.Log($"<color=red>[Battle]</color> Elimination globale détectée. Fin du combat.");
                EndBattle();
                return;
            }
        }

        // Ticks d'initiative autorisés sur TOUS les clients pour la prédiction locale !
        // Gestion du temps de combat 
        _tickTimer += Time.deltaTime;
        float tickPeriod = 1f / _ticksPerSecond;

        if (_tickTimer >= tickPeriod)
        {
            // Process as many ticks as needed to catch up with Time.deltaTime (especially on 8x speed)
            int ticksToProcess = Mathf.FloorToInt(_tickTimer / tickPeriod);
            
            // Safety cap to prevent lockups on massive lag spikes
            if (ticksToProcess > 30) ticksToProcess = 30;

            for (int i = 0; i < ticksToProcess; i++)
            {
                PerformBattleTick();
            }
            
            _tickTimer -= ticksToProcess * tickPeriod;
        }

#if UNITY_EDITOR
        // Debug : mise à jour de la liste d'engagements pour l'Inspector
        UpdateDebugEngagements();
#endif
    }

#if UNITY_EDITOR
    private void UpdateDebugEngagements()
    {
        _debugEngagements.Clear();
        if (_engagementCoordinator == null || _engagementCoordinator.ActiveEngagements == null) return;

        var engagements = _engagementCoordinator.ActiveEngagements;
        for (int i = 0; i < engagements.Count; i++)
        {
            var e = engagements[i];
            string groupA = string.Join(", ", e.GroupA.Members
                .Where(m => m != null)
                .Select(m => m.CharacterName));
            string groupB = string.Join(", ", e.GroupB.Members
                .Where(m => m != null)
                .Select(m => m.CharacterName));
            _debugEngagements.Add($"[{i}] A: [{groupA}]  vs  B: [{groupB}]");
        }
    }
#endif

    private void PerformBattleTick()
    {
        _engagementCoordinator?.CleanupEngagements();

        foreach (var character in _allParticipants)
        {
            if (character == null || !character.IsAlive()) continue;

            if (character.CharacterCombat != null)
            {
                character.CharacterCombat.UpdateInitiativeTick();
            }
        }
    }



    private void RegisterParticipants()
    {
        _allParticipants.Clear();

        foreach (var team in _teams)
        {
            foreach (var character in team.CharacterList)
            {
                if (character == null) continue;
                RegisterCharacter(character);
            }
        }
    }

    private void RegisterCharacter(Character character)
    {
        if (_allParticipants.Contains(character)) return;

        _allParticipants.Add(character);

        if (character.CharacterCombat != null)
        {
            character.CharacterCombat.JoinBattle(this);
            character.CharacterCombat.ConsumeInitiative();

            // Auto-engage ALL characters (including players) with their best enemy.
            // For players, the PlayerController can override this later via SetPlannedTarget.
            Character bestEnemy = _engagementCoordinator?.GetBestTargetFor(character);
            if (bestEnemy != null)
            {
                _engagementCoordinator?.RequestEngagement(character, bestEnemy);
            }
        }

        character.OnIncapacitated -= HandleCharacterIncapacitated;
        character.OnIncapacitated += HandleCharacterIncapacitated;
        character.OnDeath -= HandleCharacterIncapacitated;
        character.OnDeath += HandleCharacterIncapacitated;

        UpdateBattleZoneWith(character);
        Debug.Log($"<color=white>[Battle]</color> {character.CharacterName} joined combat. IsInBattle={character.CharacterCombat?.IsInBattle}. IsServer={IsServer}");

        OnParticipantAdded?.Invoke(character);
    }

    public void AddParticipant(Character newParticipant, Character target, bool asAlly = false)
    {
        if (!IsServer) return;
        if (newParticipant == null || target == null || _isBattleEnded) return;

        AddParticipantInternal(newParticipant, target, asAlly);

        ulong newParticipantId = newParticipant.NetworkObject != null ? newParticipant.NetworkObject.NetworkObjectId : 0;
        ulong targetId = target.NetworkObject != null ? target.NetworkObject.NetworkObjectId : 0;

        // Compute the server-authoritative team index AFTER placement.
        // This makes the client independent of GetTeamOf(target) which would fail if
        // InitializeClientRpc hasn't arrived yet (RPC race condition).
        BattleTeam assignedTeam = GetTeamOf(newParticipant);
        int teamIndex = (assignedTeam == _battleTeamInitiator) ? 0 : 1;

        if (newParticipantId > 0 && targetId > 0)
        {
            AddParticipantClientRpc(newParticipantId, targetId, teamIndex);
        }
    }

    [ClientRpc]
    private void AddParticipantClientRpc(ulong newParticipantId, ulong targetId, int teamIndex)
    {
        if (IsServer) return;

        Debug.Log($"<color=cyan>[Battle Client]</color> AddParticipantClientRpc received. newId={newParticipantId} targetId={targetId} teamIndex={teamIndex}");

        // LAZY INIT: If InitializeClientRpc hasn't arrived yet (RPC ordering race condition),
        // create teams and coordinator now so the participant can be properly registered.
        EnsureClientInitialized();

        if (!Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(newParticipantId, out var pObj) ||
            !Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var tObj))
        {
            Debug.LogError($"<color=red>[Battle Client]</color> AddParticipantClientRpc: could not resolve NetworkObjects.");
            return;
        }

        Character newParticipant = pObj.GetComponent<Character>();
        Character target = tObj.GetComponent<Character>();

        if (newParticipant == null || target == null)
        {
            Debug.LogError($"<color=red>[Battle Client]</color> AddParticipantClientRpc: Character component missing.");
            return;
        }

        // Place new participant directly into the server-determined team by index.
        // This avoids needing GetTeamOf(target) which fails when teams are empty or the target
        // hasn't been registered yet from InitializeClientRpc.
        BattleTeam team = (teamIndex == 0) ? _battleTeamInitiator : _battleTeamTarget;
        if (!team.CharacterList.Contains(newParticipant))
            team.AddCharacter(newParticipant);

        // Ensure the target is in the OPPOSITE team if not already registered.
        if (GetTeamOf(target) == null)
        {
            BattleTeam oppositeTeam = (teamIndex == 0) ? _battleTeamTarget : _battleTeamInitiator;
            oppositeTeam.AddCharacter(target);
            RegisterCharacter(target);
        }

        RegisterCharacter(newParticipant);

        // Register engagement so GetBestTargetFor works for this participant.
        _engagementCoordinator?.RequestEngagement(newParticipant, target);

        Debug.Log($"<color=cyan>[Battle Client]</color> {newParticipant.CharacterName} joined team {teamIndex} vs {target.CharacterName}. IsInBattle={newParticipant.CharacterCombat?.IsInBattle}");
    }

    /// <summary>
    /// Lazily initializes teams and coordinator on the client if InitializeClientRpc
    /// hasn't arrived yet. This handles the RPC ordering race condition where
    /// AddParticipantClientRpc arrives before InitializeClientRpc.
    /// </summary>
    private void EnsureClientInitialized()
    {
        if (_engagementCoordinator == null)
            _engagementCoordinator = new CombatEngagementCoordinator(this);
        if (_zoneController == null)
            _zoneController = new BattleZoneController(this, _battleZoneModifier, _battleZoneLine, _battleZoneParticles, BuildParticleSettings(), _baseBattleZoneSize, _perParticipantGrowthRate, _participantsPerTier);
        if (_teams.Count == 0)
        {
            _battleTeamInitiator = new BattleTeam();
            _battleTeamTarget = new BattleTeam();
            _teams.Add(_battleTeamInitiator);
            _teams.Add(_battleTeamTarget);
        }
    }

    /// <summary>
    /// Server-only internal method to place a participant in the correct team.
    /// </summary>
    private void AddParticipantInternal(Character newParticipant, Character target, bool asAlly)
    {
        if (newParticipant == null || target == null || _isBattleEnded) return;

        BattleTeam targetTeam = GetTeamOf(target);
        if (targetTeam == null) return;

        if (asAlly)
        {
            targetTeam.AddCharacter(newParticipant);
        }
        else
        {
            BattleTeam enemyTeam = (targetTeam == _battleTeamInitiator) ? _battleTeamTarget : _battleTeamInitiator;
            enemyTeam.AddCharacter(newParticipant);
        }

        RegisterCharacter(newParticipant);
    }

    private void UpdateBattleZoneWith(Character character)
    {
        if (_zoneController != null)
        {
            int count = _allParticipants.Count(p => p != null);
            _zoneController.UpdateBattleZoneWith(count);
        }
    }

    #region Helpers

    private ZoneParticleSettings BuildParticleSettings()
    {
        return new ZoneParticleSettings
        {
            Rate     = _particleRate,
            Color    = _particleColor,
            Size     = _particleSize,
            Lifetime = _particleLifetime,
            DriftY   = _particleDriftY
        };
    }
    public BattleTeam GetTeamOf(Character character)
    {
        return _teams.FirstOrDefault(t => t.CharacterList.Contains(character));
    }

    public BattleTeam GetOpponentTeamOf(Character character)
    {
        BattleTeam myTeam = GetTeamOf(character);
        if (myTeam == null) 
        {
            Debug.LogWarning($"<color=red>[Battle]</color> {character.CharacterName} n'est dans aucune équipe ! Impossible de trouver son opposant.");
            return null;
        }
        return (myTeam == _battleTeamInitiator) ? _battleTeamTarget : _battleTeamInitiator;
    }

    /// <summary>
    /// Passthrough vers le coordinateur pour trouver le meilleur ennemi à cibler.
    /// </summary>
    public Character GetBestTargetFor(Character attacker)
    {
        return _engagementCoordinator?.GetBestTargetFor(attacker);
    }

    public CombatEngagement RequestEngagement(Character attacker, Character target)
    {
        return _engagementCoordinator?.RequestEngagement(attacker, target);
    }
    #endregion

    private void HandleCharacterIncapacitated(Character incapacitatedCharacter)
    {
        if (_isBattleEnded) return;

        // --- NOUVEAU : DIAGNOSTIC DE FIN DE COMBAT ---
        int initiatorAlive = _battleTeamInitiator.CharacterList.Count(c => c != null && c.IsAlive());
        int targetAlive = _battleTeamTarget.CharacterList.Count(c => c != null && c.IsAlive());
        
        string initiatorNames = string.Join(", ", _battleTeamInitiator.CharacterList.Where(c => c != null && c.IsAlive()).Select(c => c.CharacterName));
        string targetNames = string.Join(", ", _battleTeamTarget.CharacterList.Where(c => c != null && c.IsAlive()).Select(c => c.CharacterName));

        Debug.Log($"<color=white>[Battle Check]</color> {incapacitatedCharacter.CharacterName} tombe. " +
                  $"Team Initiateur : {initiatorAlive}/{_battleTeamInitiator.CharacterList.Count} ({initiatorNames}) | " +
                  $"Team Cible : {targetAlive}/{_battleTeamTarget.CharacterList.Count} ({targetNames})");

        // (La vérification globale d'élimination est maintenant gérée passivement dans l'Update)

        // On libère simplement les slots d'engagements liés à ce personnage.
        RedirectIncapacitated(incapacitatedCharacter);
    }

    private void RedirectIncapacitated(Character victim)
    {
        _engagementCoordinator?.LeaveCurrentEngagement(victim);
        _engagementCoordinator?.CleanupEngagements();
    }


    public bool AreOpponents(Character a, Character b)
    {
        if (a == null || b == null || a == b) return false;
        
        BattleTeam teamA = GetTeamOf(a);
        BattleTeam teamB = GetTeamOf(b);

        return teamA != null && teamB != null && teamA != teamB;
    }

    public void EndBattle()
    {
        if (_isBattleEnded) return;
        _isBattleEnded = true;

        foreach (var character in _allParticipants)
        {
            if (character != null)
            {
                try 
                {
                    if (character.CharacterCombat != null)
                    {
                        character.CharacterCombat.LeaveBattle();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"<color=red>[Battle]</color> Exception non-critique durant LeaveBattle pour {character.CharacterName} : {e.Message}. Le nettoyage continue.");
                }
                
                _engagementCoordinator?.LeaveCurrentEngagement(character);
            }
        }
        
        _engagementCoordinator?.ClearAll();

        Debug.Log("<color=red>[Battle]</color> Le combat est TERMINÉ.");
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
        else 
            Destroy(gameObject);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (!IsServer)
        {
            foreach (var character in _allParticipants)
            {
                if (character != null && character.CharacterCombat != null)
                {
                    character.CharacterCombat.LeaveBattle();
                }
            }
            _engagementCoordinator?.ClearAll();
        }
    }

    private void OnDestroy()
    {
        if (_allParticipants != null)
        {
            foreach (var character in _allParticipants)
            {
                if (character != null)
                {
                    character.OnIncapacitated -= HandleCharacterIncapacitated;
                    character.OnDeath -= HandleCharacterIncapacitated;
                }
            }
        }
    }

    public void DrawBattleZoneOutline()
    {
        _zoneController?.DrawBattleZoneOutline();
    }
}
