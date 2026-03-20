using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CharacterCombat : CharacterSystem
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

    protected override void Awake()
    {
        base.Awake();
        CombatStyleSO defaultStyle = Resources.Load<CombatStyleSO>("Data/CombatStyle/Barehands_NoStyle");
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
        if (_character.Stats != null && _character.Stats.Initiative != null)
        {
            _character.Stats.Initiative.ResetInitiative();
        }
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
        
        _lastCombatActionTime = Time.time;
        ChangeCombatMode(true);

        if (_character.CharacterActions == null) return false;

        // Si le style actuel est Ranged et que la cible est au-delà de la portée melee, tir à distance
        if (target != null 
            && _currentCombatStyleExpertise?.Style is RangedCombatStyleSO rangedStyle)
        {
            float distToTarget = Vector3.Distance(_character.transform.position, target.transform.position);
            if (distToTarget > rangedStyle.MeleeRange)
            {
                return RangedAttack(target, rangedStyle);
            }
        }

        // Sinon, attaque melee (même pour une arme ranged au corps-à-corps)
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
        
        _character.Stats.Initiative.IncreaseCurrentAmount(totalGain);
    }

    public void JoinBattle(BattleManager manager)
    {
        _currentBattleManager = manager;
        ChangeCombatMode(true);
    }

    public void JoinBattleAsAlly(Character friend)
    {
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
        _currentBattleManager = null;
        _lastCombatActionTime = Time.time;

        // On ne force plus la sortie du mode combat ici. 
        // Le timeout de 7 secondes dans Update() s'en chargera naturellement.

        OnBattleLeft?.Invoke();
    }

    public void StartFight(Character target)
    {
        if (target == null) return;

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
                bool sameParty = _character.CurrentParty != null && _character.CurrentParty == p.CurrentParty;

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
                bool sameParty = target.CurrentParty != null && target.CurrentParty == p.CurrentParty;

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
    public void SpawnCombatStyleAttackInstance()
    {
        if (_currentCombatStyleExpertise == null || _currentCombatStyleExpertise.Style == null) return;
        
        // Seuls les styles melee ont un hitbox prefab
        if (_currentCombatStyleExpertise.Style is not MeleeCombatStyleSO meleeStyle) return;

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
    }

    public void DespawnCombatStyleAttackInstance()
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
                }
            }
            else
            {
                // Sinon on s'assure d'être en mode civil
                if (animHandler.CivilAnimatorController != null)
                {
                    animHandler.Animator.runtimeAnimatorController = animHandler.CivilAnimatorController;
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

        bool wasAlive = _character.IsAlive();
        
        float hpBefore = _character.Stats.Health.CurrentAmount;
        _character.Stats.Health.DecreaseCurrentAmount(amount);
        float hpAfter = Mathf.Max(0f, _character.Stats.Health.CurrentAmount);
        float actualDamageDealt = hpBefore - hpAfter;
        _lastCombatActionTime = Time.time;
        
        // La méthode ChangeCombatMode déclenche naturellement HandleCombatStateChanged 
        // dans les sous-systèmes, ce qui coupera automatiquement les interactions/actions en cours.
        ChangeCombatMode(true);

        OnDamageTaken?.Invoke(amount, type);

        if (_character.CharacterVisual != null && _character.CharacterVisual.CharacterBlink != null)
        {
            _character.CharacterVisual.CharacterBlink.Blink();
        }

        if (_character.Stats.Health.CurrentAmount <= 0)
        {
            _character.SetUnconscious(true);
        }

        // --- PROGRESSION EXP ---
        if (source != null && source.CharacterCombatLevel != null && actualDamageDealt > 0)
        {
            int targetLevel = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.CurrentLevel : 1;
            int targetYield = _character.CharacterCombatLevel != null ? _character.CharacterCombatLevel.BaseExpYield : 10;
            
            float maxHp = Mathf.Max(1f, _character.Stats.Health.MaxValue);
            float damagePercentage = actualDamageDealt / maxHp;

            bool isKill = wasAlive && !_character.IsAlive();
            
            int expGained = source.CharacterCombatLevel.CalculateCombatExp(targetLevel, isKill, damagePercentage, targetYield);
            source.CharacterCombatLevel.AddExperience(expGained);
        }
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
}
