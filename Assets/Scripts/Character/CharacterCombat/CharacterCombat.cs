using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class CharacterCombat : CharacterSystem, ICharacterSaveData<CombatSaveData>
{
    [Header("Expertise & Memory")]
    [SerializeField] private List<CombatStyleExpertise> _knownStyles = new List<CombatStyleExpertise>();
    [SerializeField] private CombatStyleExpertise _currentCombatStyleExpertise;
    
    public IReadOnlyList<CombatStyleExpertise> KnownStyles => _knownStyles;
    [SerializeField] private bool _isCombatMode = false;
    [SerializeField] private int _bonusMeleeMaxTargets = 0;

    public event Action<bool> OnCombatModeChanged;
    public event Action<float, DamageType> OnDamageTaken;
    public event Action OnBattleLeft;
    public event Action<BattleManager> OnBattleJoined;

    [Header("Initiative Scaling")]
    [SerializeField] private float _baseInitiativePerTick = 1f;
    [SerializeField] private float _speedMultiplierInitiative = 0.1f;


    [Header("Battle Management")]
    [SerializeField] private BattleManager _currentBattleManager;
    [SerializeField] private GameObject _battleManagerPrefab;

    private Dictionary<WeaponType, CombatStyleExpertise> _selectedStyles = new Dictionary<WeaponType, CombatStyleExpertise>();
    private GameObject _activeCombatStyleInstance;
    private float _lastCombatActionTime;
    private const float COMBAT_MODE_TIMEOUT = 7f;

    public CombatStyleExpertise CurrentCombatStyleExpertise => _currentCombatStyleExpertise;
    public bool IsCombatMode => _isCombatMode;

    [Header("Combat Intents")]
    public Func<bool> PlannedAction { get; private set; }
    public Character PlannedTarget { get; private set; }
    public event Action<Character, Func<bool>> OnActionIntentDecided;

    public void SetActionIntent(Func<bool> action, Character target)
    {
        PlannedAction = action;
        // Route through SetPlannedTarget for the full targeting chain
        // (look target, graph update, engagement evaluation)
        SetPlannedTarget(target);

        OnActionIntentDecided?.Invoke(target, action);
    }

    public void ClearActionIntent()
    {
        Debug.Log($"<color=cyan>[Combat]</color> {_character.CharacterName} ClearActionIntent → PlannedTarget: {PlannedTarget?.CharacterName ?? "null"} → null");
        PlannedAction = null;
        PlannedTarget = null;

        // Only clear look target outside of battle.
        // During combat, the character should keep facing their target between actions
        // (prevents turning away during step-back after melee attacks).
        if (_character != null && _character.CharacterVisual != null && !IsInBattle)
        {
            _character.CharacterVisual.ClearLookTarget();
        }
    }

    /// <summary>
    /// <summary>
    /// Single entry point for ALL target changes (player click, NPC AI, BT, battle join).
    /// Updates look target, targeting graph, evaluates engagements, and triggers reposition.
    /// </summary>
    public void SetPlannedTarget(Character target)
    {
        // Never self-target
        if (target == _character) return;

        PlannedTarget = target;

        // Update look target so this character faces their chosen target
        if (_character != null && _character.CharacterVisual != null)
        {
            if (target != null)
                _character.CharacterVisual.SetLookTarget(target);
            else
                _character.CharacterVisual.ClearLookTarget();
        }

        // Update targeting graph + re-evaluate engagements immediately
        if (target != null && IsInBattle && CurrentBattleManager != null)
        {
            CurrentBattleManager.SetTargeting(_character, target);
            CurrentBattleManager.Coordinator?.EvaluateEngagements();

            // Immediately start moving toward the target.
            // CombatAILogic will refine the destination once it starts ticking.
            if (_character.CharacterMovement != null)
            {
                _character.CharacterMovement.Resume();
                _character.CharacterMovement.SetDestination(target.transform.position);
            }
        }
    }

    public bool HasPlannedAction => PlannedAction != null;

    protected override void Awake()
    {
        base.Awake();
        CombatStyleSO defaultStyle = Resources.Load<CombatStyleSO>("Data/CombatStyle/Barehands_Nameless");
        if (defaultStyle != null)
        {
            if (!_knownStyles.Any(e => e.Style == defaultStyle))
            {
                _knownStyles.Add(new CombatStyleExpertise(defaultStyle));
            }
        }

        // Initialisation par défaut si possible
        if (_currentCombatStyleExpertise == null || _currentCombatStyleExpertise.Style == null)
        {
            _currentCombatStyleExpertise = _knownStyles.FirstOrDefault(s => s.WeaponType == WeaponType.Barehands);
        }
    }

    #region Battle Logic
    public bool IsInBattle => _currentBattleManager != null;
    public BattleManager CurrentBattleManager => _currentBattleManager;
    public bool IsReadyToAct => _character.Stats != null && _character.Stats.Initiative != null && _character.Stats.Initiative.IsReady();

    public void ConsumeInitiative()
    {
        if (_character.Stats == null || _character.Stats.Initiative == null) return;
        _character.Stats.Initiative.ResetInitiative();

        // Sync reset to all clients so initiative ring visuals update for remote characters.
        if (IsServer && IsSpawned)
            SyncInitiativeResetClientRpc();
    }

    [Rpc(SendTo.NotServer)]
    private void SyncInitiativeResetClientRpc()
    {
        if (_character.Stats != null && _character.Stats.Initiative != null)
            _character.Stats.Initiative.ResetInitiative();
    }

    public void UpdateInitiativeTick(float tickAmount)
    {
        if (_character.Stats != null && _character.Stats.Initiative != null)
        {
            _character.Stats.Initiative.IncreaseCurrentAmount(tickAmount);
        }
    }

    private void Update()
    {
        if (_isCombatMode && !IsInBattle)
        {
            if (Time.time - _lastCombatActionTime > COMBAT_MODE_TIMEOUT)
            {
                ChangeCombatMode(false);
                Debug.Log("<color=cyan>[Combat]</color> Mode Combat expire : DESACTIVE");
            }
        }
    }

    private void ChangeCombatMode(bool enabled)
    {
        if (_isCombatMode == enabled) return;

        _isCombatMode = enabled;
        RefreshCurrentAnimator();
        
        // Broadcast the change to all other subsystems securely via the Character hub
        _character.SetCombatState(enabled);
        OnCombatModeChanged?.Invoke(enabled);

        if (!enabled && _character.Stats != null && _character.Stats.Initiative != null)
        {
            // Remplir l'initiative pour être prêt au prochain combat même en cas de raté
            _character.Stats.Initiative.IncreaseCurrentAmount(_character.Stats.Initiative.CurrentValue);
        }
    }
    #endregion

    #region Action Methods
    public bool ExecuteAction(System.Func<bool> combatAction)
    {
        if (combatAction != null && combatAction.Invoke())
        {
            ConsumeInitiative();
            return true;
        }
        return false;
    }

    public bool Attack(Character target = null)
    {
        if (!_character.IsAlive()) return false;
        
        ulong targetId = (target != null && target.NetworkObject != null) ? target.NetworkObject.NetworkObjectId : 0;
        bool isFacingRight = _character.CharacterVisual != null ? _character.CharacterVisual.IsFacingRight : true;

        if (IsOwner)
        {
            // Owner locally predicts the visual attack
            bool success = ExecuteAttackLocally(target);
            if (!success) return false;

            if (!IsServer)
            {
                RequestAttackRpc(targetId, isFacingRight);
            }
            else
            {
                BroadcastAttackRpc(targetId, isFacingRight);
            }
            return true;
        }
        else if (IsServer) // NPC or Server-controlled entity
        {
            bool success = ExecuteAttackLocally(target);
            if (!success) return false;

            BroadcastAttackRpc(targetId, isFacingRight);
            return true;
        }

        return false;
    }

    [Rpc(SendTo.Server)]
    private void RequestAttackRpc(ulong targetNetworkObjectId, bool isFacingRight)
    {
        Character target = null;
        if (targetNetworkObjectId > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var netObj))
            target = netObj.GetComponent<Character>();

        if (_character.CharacterVisual != null) _character.CharacterVisual.IsFacingRight = isFacingRight;

        ExecuteAttackLocally(target);
        BroadcastAttackRpc(targetNetworkObjectId, isFacingRight);
    }

    [Rpc(SendTo.NotServer)]
    private void BroadcastAttackRpc(ulong targetNetworkObjectId, bool isFacingRight)
    {
        if (IsOwner) return; // Owner already predicted it

        Character target = null;
        if (targetNetworkObjectId > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var netObj))
            target = netObj.GetComponent<Character>();

        if (_character.CharacterVisual != null) _character.CharacterVisual.IsFacingRight = isFacingRight;

        ExecuteAttackLocally(target);
    }

    private bool ExecuteAttackLocally(Character target)
    {
        _lastCombatActionTime = Time.time;
        _hitboxSpawnedForAction = false;
        ChangeCombatMode(true);

        if (_character.CharacterActions == null) return false;

        // Consume initiative on the executor (Owner predicts, Server validates+broadcasts)
        ConsumeInitiative();

        if (target != null
            && _currentCombatStyleExpertise?.Style is RangedCombatStyleSO rangedStyle)
        {
            float distToTarget = Vector3.Distance(_character.transform.position, target.transform.position);
            if (distToTarget > rangedStyle.MeleeRange)
            {
                return RangedAttack(target, rangedStyle);
            }
        }

        return MeleeAttack();
    }

    public bool MeleeAttack()
    {
        if (_character.CharacterActions == null) return false;
        return _character.CharacterActions.ExecuteAction(new CharacterMeleeAttackAction(_character));
    }

    public bool RangedAttack(Character target, RangedCombatStyleSO rangedStyle)
    {
        if (_character.CharacterActions == null || target == null || rangedStyle == null) return false;
        return _character.CharacterActions.ExecuteAction(new CharacterRangedAttackAction(_character, target, rangedStyle));
    }

    public void ToggleCombatMode()
    {
        ChangeCombatMode(!_isCombatMode);
        if (_isCombatMode) _lastCombatActionTime = Time.time;
    }
    #endregion

    #region Ability Execution

    /// <summary>
    /// Uses an ability from the character's active ability slot.
    /// Follows the same Owner-predict → Server-validate → Broadcast pattern as Attack().
    /// </summary>
    public bool UseAbility(int slotIndex, Character target = null)
    {
        if (!_character.IsAlive()) return false;
        if (_character.CharacterAbilities == null) return false;

        AbilityInstance ability = _character.CharacterAbilities.GetActiveSlot(slotIndex);
        if (ability == null) return false;

        ulong targetId = target != null && target.NetworkObject != null ? target.NetworkObject.NetworkObjectId : 0;

        if (IsOwner)
        {
            bool success = ExecuteAbilityLocally(ability, target);
            if (!success) return false;
            if (!IsServer) RequestUseAbilityRpc(slotIndex, targetId);
            else BroadcastUseAbilityRpc(slotIndex, targetId);
            return true;
        }
        else if (IsServer)
        {
            bool success = ExecuteAbilityLocally(ability, target);
            if (!success) return false;
            BroadcastUseAbilityRpc(slotIndex, targetId);
            return true;
        }

        return false;
    }

    private bool ExecuteAbilityLocally(AbilityInstance ability, Character target)
    {
        _lastCombatActionTime = Time.time;
        ChangeCombatMode(true);

        if (_character.CharacterActions == null) return false;

        CharacterAction action = ability switch
        {
            PhysicalAbilityInstance physical => new CharacterPhysicalAbilityAction(_character, physical, target),
            SpellInstance spell => new CharacterSpellCastAction(_character, spell, target),
            _ => null
        };

        if (action == null) return false;
        return _character.CharacterActions.ExecuteAction(action);
    }

    [Rpc(SendTo.Server)]
    private void RequestUseAbilityRpc(int slotIndex, ulong targetNetworkObjectId)
    {
        if (!IsServer) return;

        Character target = null;
        if (targetNetworkObjectId > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var netObj))
            target = netObj.GetComponent<Character>();

        // Server validates and broadcasts
        AbilityInstance ability = _character.CharacterAbilities?.GetActiveSlot(slotIndex);
        if (ability == null) return;

        if (ExecuteAbilityLocally(ability, target))
        {
            BroadcastUseAbilityRpc(slotIndex, targetNetworkObjectId);
        }
    }

    [Rpc(SendTo.NotServer)]
    private void BroadcastUseAbilityRpc(int slotIndex, ulong targetNetworkObjectId)
    {
        if (IsOwner) return; // Owner already predicted locally

        Character target = null;
        if (targetNetworkObjectId > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var netObj))
            target = netObj.GetComponent<Character>();

        AbilityInstance ability = _character.CharacterAbilities?.GetActiveSlot(slotIndex);
        if (ability != null)
        {
            ExecuteAbilityLocally(ability, target);
        }
    }

    #endregion

    #region Equipment Bridge
    public void OnWeaponChanged(WeaponInstance weapon)
    {
        WeaponType type = (weapon != null && weapon.ItemSO is WeaponSO weaponSO) ? weaponSO.WeaponType : WeaponType.Barehands;
        
        // Trouver le meilleur style pour cette arme
        _currentCombatStyleExpertise = _knownStyles.FirstOrDefault(s => s.WeaponType == type);
        
        if (_currentCombatStyleExpertise == null)
        {
            // Fallback barehands
            _currentCombatStyleExpertise = _knownStyles.FirstOrDefault(s => s.WeaponType == WeaponType.Barehands);
        }

        RefreshCurrentAnimator();
    }
    #endregion

    #region Battle Lifecycle
    public void UpdateInitiativeTick()
    {
        if (_character.Stats == null || _character.Stats.Initiative == null) return;

        float speedValue = _character.Stats.Speed != null ? _character.Stats.Speed.Value : 0f;
        
        // 1. Calcul de base
        float rawGain = _baseInitiativePerTick + (speedValue * _speedMultiplierInitiative);
        
        // 2. On plafonne à 2.0 (On prend la valeur la plus petite entre le calcul et 2.0)
        float cappedGain = Mathf.Min(rawGain, 2.0f);
        
        // 3. On applique le Random Range sur la valeur plafonnée
        float totalGain = cappedGain * UnityEngine.Random.Range(0.7f, 1.3f);
        
        bool wasReady = _character.Stats.Initiative.IsReady();
        _character.Stats.Initiative.IncreaseCurrentAmount(totalGain);

        // Passive trigger: OnInitiativeFull (fire once when initiative becomes ready)
        if (!wasReady && _character.Stats.Initiative.IsReady())
        {
            _character.CharacterAbilities?.OnPassiveTriggerEvent(PassiveTriggerCondition.OnInitiativeFull, _character, null);
        }
    }

    public void JoinBattle(BattleManager manager)
    {
        _currentBattleManager = manager;
        ChangeCombatMode(true);

        // Passive trigger: OnBattleStart
        _character.CharacterAbilities?.OnPassiveTriggerEvent(PassiveTriggerCondition.OnBattleStart, _character, null);

        OnBattleJoined?.Invoke(manager);
    }

    public void JoinBattleAsAlly(Character friend)
    {
        if (!IsServer) return; // Only Server manages physical battles
        if (friend == null || !friend.CharacterCombat.IsInBattle) return;
        if (IsInBattle) return;

        // --- SÉCURITÉ : On ne rejoint que si on est LIBRE ---
        if (!_character.IsFree())
        {
            Debug.Log($"<color=orange>[Combat]</color> {_character.CharacterName} est trop occupé pour rejoindre son ami {friend.CharacterName}.");
            return;
        }

        if (!_character.IsAlive()) return;

        _currentBattleManager = friend.CharacterCombat.CurrentBattleManager;
        _currentBattleManager.AddParticipant(_character, friend, asAlly: true);
    }

    public void LeaveBattle()
    {
        // Clear combat intent and look target BEFORE nulling the battle manager,
        // so ClearActionIntent sees IsInBattle as false and properly clears everything.
        _currentBattleManager = null;
        PlannedAction = null;
        PlannedTarget = null;
        _character.CharacterVisual?.ClearLookTarget();

        _lastCombatActionTime = Time.time;

        // Le timeout de 7 secondes dans Update() s'en chargera naturellement.

        OnBattleLeft?.Invoke();
    }

    public void StartFight(Character target)
    {
        if (target == null) return;
        
        if (!IsServer) return; // ONLY SERVER CAN SPAWN/MANAGE BATTLES

        // --- PÉNALITÉ DE RELATION ---
        // On baisse la relation des deux côtés de 10 points car un combat commence
        if (_character.CharacterRelation != null)
            _character.CharacterRelation.UpdateRelation(target, -10);
        
        if (target.CharacterRelation != null)
            target.CharacterRelation.UpdateRelation(_character, -10);

        if (target.CharacterCombat.IsInBattle)
        {
            if (IsInBattle) return;
            if (!_character.IsAlive()) return;

            target.CharacterCombat.CurrentBattleManager.AddParticipant(_character, target);
            this._currentBattleManager = target.CharacterCombat.CurrentBattleManager;
            return;
        }

        // --- NOUVEAU : VÉRIFICATION DYNAMIQUE DE FUSION (BASÉE PHYSIQUE) ---
        // On cherche un collider "BattleZone" à proximité (ex: 25m) pour éviter les registres statiques globaux
        BattleManager nearbyBattle = null;
        Character connectionFound = null;
        bool initiatorHasLink = false;

        Collider[] hitColliders = Physics.OverlapSphere(_character.transform.position, 25f);
        List<BattleManager> detectedBattles = new List<BattleManager>();

        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("BattleZone"))
            {
                BattleManager bm = hit.GetComponent<BattleManager>() ?? hit.GetComponentInParent<BattleManager>();
                if (bm != null && !bm.IsBattleEnded && !detectedBattles.Contains(bm))
                {
                    detectedBattles.Add(bm);
                }
            }
        }

        foreach (var battle in detectedBattles)
        {
            // On vérifie si l'initiateur a un lien
            foreach (var p in battle.BattleTeams.SelectMany(t => t.CharacterList))
            {
                bool isFriend = _character.CharacterRelation != null && _character.CharacterRelation.IsFriend(p);
                bool sameParty = _character.CharacterParty != null && _character.CharacterParty.IsInParty
                    && p.CharacterParty != null && p.CharacterParty.IsInParty
                    && _character.CharacterParty.PartyData.PartyId == p.CharacterParty.PartyData.PartyId;

                if (isFriend || sameParty)
                {
                    nearbyBattle = battle;
                    connectionFound = p;
                    initiatorHasLink = true;
                    break;
                }
            }
            if (nearbyBattle != null) break;

            // On vérifie si la cible a un lien
            foreach (var p in battle.BattleTeams.SelectMany(t => t.CharacterList))
            {
                bool isFriend = target.CharacterRelation != null && target.CharacterRelation.IsFriend(p);
                bool sameParty = target.CharacterParty != null && target.CharacterParty.IsInParty
                    && p.CharacterParty != null && p.CharacterParty.IsInParty
                    && target.CharacterParty.PartyData.PartyId == p.CharacterParty.PartyData.PartyId;

                if (isFriend || sameParty)
                {
                    nearbyBattle = battle;
                    connectionFound = p;
                    initiatorHasLink = false; // Le lien appartient à la cible
                    break;
                }
            }
            if (nearbyBattle != null) break;
        }

        if (nearbyBattle != null)
        {
            Debug.Log($"<color=orange>[Battle]</color> Fusion : {_character.CharacterName} et {target.CharacterName} rejoignent le combat de {connectionFound.CharacterName}");
            
            if (initiatorHasLink)
            {
                // L'initiateur rejoint son ami, la cible rejoint en face
                nearbyBattle.AddParticipant(_character, connectionFound, asAlly: true);
                nearbyBattle.AddParticipant(target, _character, asAlly: false);
            }
            else
            {
                // La cible rejoint son ami, l'initiateur rejoint en face
                nearbyBattle.AddParticipant(target, connectionFound, asAlly: true);
                nearbyBattle.AddParticipant(_character, target, asAlly: false);
            }
            return;
        }

        if (!ValidateFight(target)) return;

        GameObject instanceGo = Instantiate(_battleManagerPrefab);
        
        var netObj = instanceGo.GetComponent<NetworkObject>();
        if (netObj != null) netObj.Spawn(true);

        BattleManager manager = instanceGo.GetComponent<BattleManager>();

        if (manager == null)
        {
            Debug.LogError("<color=red>[Battle]</color> Le prefab n'a pas le script BattleManager !");
            Destroy(instanceGo);
            return;
        }

        this._currentBattleManager = manager;
        manager.Initialize(_character, target);
    }

    private bool ValidateFight(Character target)
    {
        if (target == null) return false;
        if (!_character.IsAlive()) return false;
        if (!target.IsAlive()) return false;
        return true;
    }

    public void ForceExitCombatMode()
    {
        _currentBattleManager = null;
        PlannedAction = null;
        PlannedTarget = null;
        _character.CharacterVisual?.ClearLookTarget();
        ChangeCombatMode(false);
    }

    /// <summary>
    /// Désactive uniquement la posture de combat (animator) sans quitter le BattleManager.
    /// Utilisé pour éviter les glitches visuels lors de la mort/inconscience.
    /// </summary>
    public void ExitCombatMode()
    {
        ChangeCombatMode(false);
    }
    #endregion

    #region Animation Events
    private bool _hitboxSpawnedForAction = false;

    public void SpawnCombatStyleAttackInstance()
    {
        if (_currentCombatStyleExpertise == null || _currentCombatStyleExpertise.Style == null) return;
        
        // Seuls les styles melee ont un hitbox prefab
        if (_currentCombatStyleExpertise.Style is not MeleeCombatStyleSO meleeStyle) return;

        // If we are Owner but not Server, we tell the Server that the impact frame has been reached 
        // to guarantee it fires, bypassing unreliable synced AnimationEvents!
        if (IsOwner && !IsServer)
        {
            bool isFacingRight = _character.CharacterVisual != null ? _character.CharacterVisual.IsFacingRight : true;
            RequestSpawnHitboxServerRpc(isFacingRight);
        }

        if (IsServer)
        {
            SpawnHitboxNatively(); // Fallback for Host checking its own events, or receiving them
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestSpawnHitboxServerRpc(bool isFacingRight)
    {
        SpawnHitboxNatively();
    }

    private void SpawnHitboxNatively()
    {
        if (_currentCombatStyleExpertise == null || _currentCombatStyleExpertise.Style == null) return;
        if (_currentCombatStyleExpertise.Style is not MeleeCombatStyleSO meleeStyle) return;

        // Prevent double spawn if AnimationEvent managed to fire AND RPC arrived
        if (_hitboxSpawnedForAction) return; 
        _hitboxSpawnedForAction = true;

        GameObject prefab = meleeStyle.HitboxPrefab;
        if (prefab == null) return;

        // Positionnement à l'extrémité visuelle selon la direction et centré en Y
        Vector3 spawnPos = _character.transform.position;
        if (_character.CharacterVisual != null)
        {
            Vector3 facingDir = _character.CharacterVisual.IsFacingRight ? Vector3.right : Vector3.left;
            spawnPos = _character.CharacterVisual.GetVisualExtremity(facingDir);
            spawnPos.z = _character.transform.position.z; // Rester sur le même plan Z
        }

        _activeCombatStyleInstance = Instantiate(prefab, spawnPos, Quaternion.identity, _character.transform);
        var attackScript = _activeCombatStyleInstance.GetComponent<CombatStyleAttack>();
        if (attackScript != null)
        {
            attackScript.Initialize(_character, _bonusMeleeMaxTargets);
        }

        // --- SAFETY TIMEOUT ---
        // Ensure the hitbox gets destroyed anyway if the despawn animation event is interrupted (stun, death, lag)
        Destroy(_activeCombatStyleInstance, 2.0f);
    }

    public void DespawnCombatStyleAttackInstance()
    {
        if (IsOwner && !IsServer)
        {
            RequestDespawnHitboxServerRpc();
        }

        if (IsServer)
        {
            DespawnHitboxNatively();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestDespawnHitboxServerRpc()
    {
        DespawnHitboxNatively();
    }

    private void DespawnHitboxNatively()
    {
        if (_activeCombatStyleInstance != null)
        {
            Destroy(_activeCombatStyleInstance);
            _activeCombatStyleInstance = null;
        }
    }
    #endregion

    private void RefreshCurrentAnimator()
    {
        if (_character.CharacterVisual != null && _character.CharacterVisual.CharacterAnimator != null)
        {
            var animHandler = _character.CharacterVisual.CharacterAnimator;

            // On n'applique le contrôleur de combat QUE si on est en mode combat
            if (_isCombatMode && _currentCombatStyleExpertise != null)
            {
                var controller = _currentCombatStyleExpertise.GetCurrentAnimator();
                if (controller != null)
                {
                    animHandler.Animator.runtimeAnimatorController = controller;
                    animHandler.CacheParameters();
                    animHandler.CacheClipDurations();
                }
            }
            else
            {
                // Sinon on s'assure d'être en mode civil
                if (animHandler.CivilAnimatorController != null)
                {
                    animHandler.Animator.runtimeAnimatorController = animHandler.CivilAnimatorController;
                    animHandler.CacheParameters();
                    animHandler.CacheClipDurations();
                }
            }

            // --- NOUVEAU : SYNC DES PARAMÈTRES APRÈS SWAP ---
            // Cela évite que le perso ne se "relève" si le controller reset les booléens par défaut
            animHandler.SyncParameters(_character, _isCombatMode);
        }
    }

    public void TakeDamage(float amount, DamageType type = DamageType.Blunt, Character source = null)
    {
        if (_character.Stats == null || _character.Stats.Health == null || !_character.IsAlive()) return;

        // ONLY the Server executes the raw physical damage calculation and EXP
        if (!IsServer) return;

        bool wasAlive = _character.IsAlive();
        float hpBefore = _character.Stats.Health.CurrentAmount;
        
        _character.Stats.Health.DecreaseCurrentAmount(amount);
        float hpAfter = Mathf.Max(0f, _character.Stats.Health.CurrentAmount);
        float actualDamageDealt = hpBefore - hpAfter;
        
        _lastCombatActionTime = Time.time;
        ChangeCombatMode(true);

        OnDamageTaken?.Invoke(amount, type);

        if (_character.CharacterVisual != null && _character.CharacterVisual.CharacterBlink != null)
        {
            _character.CharacterVisual.CharacterBlink.Blink();
        }

        // --- PASSIVE TRIGGERS: OnDamageTaken ---
        _character.CharacterAbilities?.OnPassiveTriggerEvent(PassiveTriggerCondition.OnDamageTaken, source, _character);

        // --- PASSIVE TRIGGERS: OnLowHPThreshold ---
        _character.CharacterAbilities?.OnPassiveTriggerEvent(PassiveTriggerCondition.OnLowHPThreshold, source, _character);

        if (hpAfter <= 0)
        {
            _character.SetUnconscious(true);

            // --- PASSIVE TRIGGERS: OnKill (notify the source) ---
            if (wasAlive && source != null)
            {
                source.CharacterAbilities?.OnPassiveTriggerEvent(PassiveTriggerCondition.OnKill, source, _character);
            }
        }

        // --- PROGRESSION EXP ---
        bool isKill = wasAlive && !_character.IsAlive();
        if (source != null && source.CharacterCombatLevel != null && actualDamageDealt > 0)
        {
            int targetLevel = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.CurrentLevel : 1;
            int targetYield = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.BaseExpYield : 10;

            float maxHp = Mathf.Max(1f, _character.Stats.Health.MaxValue);
            float damagePercentage = actualDamageDealt / maxHp;
            
            int expGained = source.CharacterCombatLevel.CalculateCombatExp(targetLevel, isKill, damagePercentage, targetYield);
            source.CharacterCombatLevel.AddExperience(expGained);
        }

        // Sync HP change and visuals to all clients
        ulong sourceId = (source != null && source.NetworkObject != null) ? source.NetworkObject.NetworkObjectId : 0;
        SyncDamageClientRpc(amount, type, hpAfter, sourceId);
    }

    [Rpc(SendTo.NotServer)]
    private void SyncDamageClientRpc(float amount, DamageType type, float serverHpAfter, ulong sourceId)
    {
        if (_character.Stats == null || _character.Stats.Health == null) return;

        // Force exactly the Server's HP
        _character.Stats.Health.CurrentAmount = serverHpAfter;

        _lastCombatActionTime = Time.time;
        ChangeCombatMode(true);

        OnDamageTaken?.Invoke(amount, type);

        if (_character.CharacterVisual != null && _character.CharacterVisual.CharacterBlink != null)
        {
            _character.CharacterVisual.CharacterBlink.Blink();
        }

        if (serverHpAfter <= 0)
        {
            _character.SetUnconscious(true);
        }

        // Note: Clients do not execute EXP progression logic. That remains Server-only.
    }

    // Keep compatibility with old single arg call if needed
    public void TakeDamage(float amount) => TakeDamage(amount, DamageType.Blunt, null);

    public void UnlockCombatStyle(CombatStyleSO style)
    {
        if (style == null) return;
        if (!_knownStyles.Exists(s => s.Style == style))
        {
            _knownStyles.Add(new CombatStyleExpertise(style));
            Debug.Log($"<color=yellow>[Combat]</color> Nouveau style débloqué : {style.StyleName}");
        }
    }

    /// <summary>
    /// Unlocks a known combat style at a specific starting level. Used by dev-mode spawn
    /// and by save/load restore. XP starts at 0. No-op if the style is already known.
    /// </summary>
    public void UnlockCombatStyle(CombatStyleSO style, int level)
    {
        if (style == null) return;
        if (_knownStyles.Exists(s => s.Style == style))
        {
            Debug.LogWarning($"<color=orange>[Combat]</color> {_character.CharacterName} already knows {style.StyleName} — ignoring dev-mode unlock.");
            return;
        }

        _knownStyles.Add(new CombatStyleExpertise(style, level, 0f));
        Debug.Log($"<color=yellow>[Combat]</color> {_character.CharacterName} learned {style.StyleName} at L{level} (dev-mode).");
    }

    #region ICharacterSaveData Implementation

    public string SaveKey => "CharacterCombat";
    public int LoadPriority => 70;

    public CombatSaveData Serialize()
    {
        var data = new CombatSaveData();

        foreach (var expertise in _knownStyles)
        {
            if (expertise.Style == null) continue;

            data.knownStyles.Add(new CombatStyleSaveEntry
            {
                styleId = expertise.Style.name,
                level = expertise.Level,
                experience = expertise.Experience
            });
        }

        if (_currentCombatStyleExpertise?.Style != null)
        {
            data.preferredStyleId = _currentCombatStyleExpertise.Style.name;
        }

        return data;
    }

    public void Deserialize(CombatSaveData data)
    {
        if (data == null) return;

        _knownStyles.Clear();

        foreach (var entry in data.knownStyles)
        {
            CombatStyleSO styleSO = Resources.Load<CombatStyleSO>($"Data/CombatStyle/{entry.styleId}");
            if (styleSO == null)
            {
                Debug.LogWarning($"[CharacterCombat] Could not find CombatStyleSO '{entry.styleId}' during deserialization. Skipping.");
                continue;
            }

            _knownStyles.Add(new CombatStyleExpertise(styleSO, entry.level, entry.experience));
        }

        // Restore preferred style
        if (!string.IsNullOrEmpty(data.preferredStyleId))
        {
            _currentCombatStyleExpertise = _knownStyles.Find(s => s.Style != null && s.Style.name == data.preferredStyleId);
        }

        // Fallback to barehands if preferred style was not found
        if (_currentCombatStyleExpertise == null)
        {
            _currentCombatStyleExpertise = _knownStyles.Find(s => s.WeaponType == WeaponType.Barehands);
        }
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

    #endregion
}
