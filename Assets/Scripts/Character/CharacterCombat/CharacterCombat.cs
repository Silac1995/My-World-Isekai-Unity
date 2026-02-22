using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CharacterCombat : MonoBehaviour
{
    [SerializeField] private Character _character;

    [Header("Expertise & Memory")]
    [SerializeField] private List<CombatStyleExpertise> _knownStyles = new List<CombatStyleExpertise>();
    [SerializeField] private CombatStyleExpertise _currentCombatStyleExpertise;
    [SerializeField] private bool _isCombatMode = false;
    [SerializeField] private int _bonusMeleeMaxTargets = 0;

    [Header("Initiative Scaling")]
    [SerializeField] private float _baseInitiativePerTick = 1f;
    [SerializeField] private float _speedMultiplierInitiative = 0.1f;

    [Header("HP Recovery Status Effects")]
    [SerializeField] private CharacterStatusEffect _unconsciousEffect;
    [SerializeField] private CharacterStatusEffect _outOfCombatEffect;

    [Header("Battle Management")]
    [SerializeField] private BattleManager _currentBattleManager;
    [SerializeField] private GameObject _battleManagerPrefab;

    private Dictionary<WeaponType, CombatStyleExpertise> _selectedStyles = new Dictionary<WeaponType, CombatStyleExpertise>();
    private GameObject _activeCombatStyleInstance;
    private float _lastCombatActionTime;
    private const float COMBAT_MODE_TIMEOUT = 7f;

    public CombatStyleExpertise CurrentCombatStyleExpertise => _currentCombatStyleExpertise;
    public bool IsCombatMode => _isCombatMode;

    private void Awake()
    {
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
                _isCombatMode = false;
                RefreshCurrentAnimator();
                Debug.Log($"<color=cyan>[Combat]</color> Mode Combat expiré : DÉSACTIVÉ");
            }
        }

        HandlePassiveRecoveryEffects();
    }

    private void HandlePassiveRecoveryEffects()
    {
        if (IsInBattle || _character.Stats == null || _character.StatusManager == null) return;

        bool shouldHaveUnconscious = _character.IsUnconscious;
        bool shouldHaveOutOfCombat = !_character.IsUnconscious && _character.IsAlive() && !_isCombatMode;

        // Effet Inconscient
        if (shouldHaveUnconscious)
        {
            if (_unconsciousEffect != null && !_character.StatusManager.HasEffect(_unconsciousEffect))
                _character.StatusManager.ApplyEffect(_unconsciousEffect);
        }
        else
        {
            if (_unconsciousEffect != null && _character.StatusManager.HasEffect(_unconsciousEffect))
                _character.StatusManager.RemoveEffect(_unconsciousEffect);
        }

        // Effet Hors Combat
        if (shouldHaveOutOfCombat)
        {
            if (_outOfCombatEffect != null && !_character.StatusManager.HasEffect(_outOfCombatEffect))
                _character.StatusManager.ApplyEffect(_outOfCombatEffect);
        }
        else
        {
            if (_outOfCombatEffect != null && _character.StatusManager.HasEffect(_outOfCombatEffect))
                _character.StatusManager.RemoveEffect(_outOfCombatEffect);
        }

        // Seuil de réveil : 30% de la vie max
        if (_character.IsUnconscious && _character.Stats.Health.CurrentAmount >= _character.Stats.Health.MaxValue * 0.3f)
        {
            _character.WakeUp();
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

    public bool Attack()
    {
        if (!_character.IsAlive()) return false;
        
        _isCombatMode = true;
        _lastCombatActionTime = Time.time;
        RefreshCurrentAnimator();
        
        if (_character.CharacterActions != null)
        {
            return _character.CharacterActions.ExecuteAction(new CharacterMeleeAttackAction(_character));
        }
        return false;
    }

    public bool MeleeAttack() => Attack();

    public void ToggleCombatMode()
    {
        _isCombatMode = !_isCombatMode;
        if (_isCombatMode) _lastCombatActionTime = Time.time;
        RefreshCurrentAnimator();
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
        float totalGain = (_baseInitiativePerTick + (speedValue * _speedMultiplierInitiative)) * Random.Range(0.7f, 1.3f);
        
        _character.Stats.Initiative.IncreaseCurrentAmount(totalGain);
    }

    public void JoinBattle(BattleManager manager)
    {
        _currentBattleManager = manager;
        _isCombatMode = true;
        RefreshCurrentAnimator();
    }

    public void LeaveBattle()
    {
        _currentBattleManager = null;
        _lastCombatActionTime = Time.time;

        if (_character.Controller != null && _character.Controller.GetCurrentBehaviour<CombatBehaviour>() != null)
        {
            _character.Controller.PopBehaviour();
        }
    }

    public void StartFight(Character target)
    {
        if (target != null && target.CharacterCombat.IsInBattle)
        {
            if (IsInBattle) return;
            if (!_character.IsAlive()) return;

            target.CharacterCombat.CurrentBattleManager.AddParticipant(_character, target);
            this._currentBattleManager = target.CharacterCombat.CurrentBattleManager;
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
        _isCombatMode = false;
        _currentBattleManager = null;
        RefreshCurrentAnimator();
    }
    #endregion

    #region Animation Events
    public void SpawnCombatStyleAttackInstance()
    {
        if (_currentCombatStyleExpertise == null || _currentCombatStyleExpertise.Style == null) return;
        
        GameObject prefab = _currentCombatStyleExpertise.Style.Prefab;
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

            animHandler.SetCombat(_isCombatMode);
        }
    }

    public void TakeDamage(float amount, MeleeDamageType type = MeleeDamageType.Blunt)
    {
        if (_character.Stats == null || _character.Stats.Health == null) return;

        _character.Stats.Health.DecreaseCurrentAmount(amount);
        _lastCombatActionTime = Time.time;
        _isCombatMode = true;

        if (_character.CharacterVisual != null && _character.CharacterVisual.CharacterBlink != null)
        {
            _character.CharacterVisual.CharacterBlink.Blink();
        }

        RefreshCurrentAnimator();

        if (_character.Stats.Health.CurrentAmount <= 0)
        {
            _character.SetUnconscious(true);
        }
    }

    // Keep compatibility with old single arg call if needed
    public void TakeDamage(float amount) => TakeDamage(amount, MeleeDamageType.Blunt);
}
