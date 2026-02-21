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

    [Header("HP Recovery Settings")]
    [SerializeField] private float _unconsciousRecoveryRate = 2.0f; // HP/s quand inconscient
    [SerializeField] private float _outOfCombatRecoveryRate = 0.2f;  // HP/s quand conscient

    // --- NOUVEAUX CHAMPS DÉPLACÉS ---
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
        // Chargement du style par défaut à mains nues
        CombatStyleSO defaultStyle = Resources.Load<CombatStyleSO>("Data/CombatStyle/Barehands_NoStyle");
        if (defaultStyle != null)
        {
            // On l'ajoute seulement s'il n'est pas déjà présent
            if (!_knownStyles.Any(e => e.Style == defaultStyle))
            {
                _knownStyles.Add(new CombatStyleExpertise(defaultStyle));
            }
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
        // --- AUTO-DESACTIVATION DU MODE COMBAT ---
        if (_isCombatMode && !IsInBattle)
        {
            if (Time.time - _lastCombatActionTime > COMBAT_MODE_TIMEOUT)
            {
                _isCombatMode = false;
                RefreshCurrentAnimator();
                Debug.Log($"<color=cyan>[Combat]</color> Mode Combat NPC expir? (Inactivit?) : D?SACTIV?");
            }
        }

        // --- RÉCUPÉRATION PASSIVE (Uniquement hors bataille) ---
        if (!IsInBattle && _character.Stats != null && _character.Stats.Health != null)
        {
            if (_character.IsUnconscious)
            {
                // Récupération rapide quand inconscient
                _character.Stats.Health.IncreaseCurrentAmount(_unconsciousRecoveryRate * Time.deltaTime);

                // Seuil de réveil : 30% de la vie max
                if (_character.Stats.Health.CurrentAmount >= _character.Stats.Health.MaxValue * 0.3f)
                {
                    _character.WakeUp();
                }
            }
            else if (_character.IsAlive())
            {
                // Récupération lente quand conscient
                _character.Stats.Health.IncreaseCurrentAmount(_outOfCombatRecoveryRate * Time.deltaTime);
            }
        }
    }

    /// <summary>
    /// Point d'entrée centralisé pour toute action de combat (Attaque, Item, Capacité).
    /// Exécute l'action et consomme l'initiative SEULEMENT si l'action a pu démarrer.
    /// </summary>
    public bool ExecuteAction(System.Func<bool> combatAction)
    {
        if (combatAction != null && combatAction.Invoke())
        {
            ConsumeInitiative();
            return true;
        }
        return false;
    }

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
        
        // On ne coupe plus le mode combat immédiatement.
        // On reset le timer pour que la persistence (7s) commence AU MOMENT où on quitte la bataille.
        _lastCombatActionTime = Time.time;

        if (_character.Controller != null && _character.Controller.GetCurrentBehaviour<CombatBehaviour>() != null)
        {
            _character.Controller.PopBehaviour();
        }

        // On ne Refresh plus ici, l'Update s'en chargera après le délai
    }

    public void StartFight(Character target)
    {
        // On permet de rejoindre un combat existant
        if (target != null && target.CharacterCombat.IsInBattle)
        {
            // Vérification de base pour l'initiateur
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

        Debug.Log($"<color=orange>[Battle]</color> {_character.CharacterName} a provoqué {target.CharacterName} !");
    }

    private bool ValidateFight(Character target)
    {
        if (target == null)
        {
            Debug.LogWarning("<color=red>[Battle]</color> Impossible de combattre : La cible est null.");
            return false;
        }

        if (!_character.IsAlive())
        {
            Debug.LogWarning($"<color=orange>[Battle]</color> {_character.CharacterName} ne peut pas attaquer car il est MORT.");
            return false;
        }

        if (!target.IsAlive())
        {
            Debug.LogWarning($"<color=orange>[Battle]</color> {_character.CharacterName} ne peut pas attaquer {target.CharacterName} car la cible est déjà MORTE.");
            return false;
        }

        if (IsInBattle)
        {
            Debug.LogWarning($"<color=yellow>[Battle]</color> {_character.CharacterName} est déjà engagé dans un combat.");
            return false;
        }

        // Suppression de la restriction : on peut maintenant rejoindre un combat existant
        // Cette validation n'est appelée que pour un NOUVEAU combat

        Debug.Log($"<color=green>[Battle]</color> Validation réussie : Nouveau combat entre {_character.CharacterName} et {target.CharacterName} possible.");
        return true;
    }
    #endregion

    public void ToggleCombatMode()
    {
        // --- S?CURIT? : On ne change pas de mode pendant une action ---
        // Cela ?viterait de reset l'Animator en plein milieu d'un coup d'?p?e
        if (_character.CharacterActions.CurrentAction != null)
        {
            Debug.LogWarning($"<color=yellow>[Combat]</color> Impossible de changer de mode : Une action est en cours.");
            return;
        }

        _isCombatMode = !_isCombatMode;
        if (_isCombatMode) _lastCombatActionTime = Time.time;
        
        RefreshCurrentAnimator();
        Debug.Log($"<color=cyan>[Combat]</color> Mode Combat : {(_isCombatMode ? "ACTIV?" : "D?SACTIV?")}");
    }

    public void ForceExitCombatMode()
    {
        _isCombatMode = false;
        _currentBattleManager = null;
        RefreshCurrentAnimator();
    }

    public void SelectStyle(CombatStyleSO styleToSelect)
    {
        CombatStyleExpertise expertise = _knownStyles.FirstOrDefault(e => e.Style == styleToSelect);

        if (expertise != null)
        {
            _selectedStyles[styleToSelect.WeaponType] = expertise;
            RefreshCurrentAnimator();
            Debug.Log($"<color=green>[Combat]</color> Style {styleToSelect.StyleName} sauvegardé pour {styleToSelect.WeaponType}");
        }
    }

    public void OnWeaponChanged(WeaponInstance weapon)
    {
        WeaponType type = WeaponType.Barehands;
            
        if (weapon != null && weapon.ItemSO is WeaponSO weaponData)
        {
            type = weaponData.WeaponType;
        }

        if (!_selectedStyles.ContainsKey(type))
        {
            AutoSelectInitialStyle(type);
        }

        RefreshCurrentAnimator();
    }

    private void AutoSelectInitialStyle(WeaponType type)
    {
        var firstMatch = _knownStyles.FirstOrDefault(e => e.GetWeaponType() == type);
        if (firstMatch != null)
        {
            _selectedStyles[type] = firstMatch;
        }
    }

    public void RefreshCurrentAnimator()
    {
        // --- S?CURIT? : On ne touche pas ? l'Animator pendant une action ---
        if (_character.CharacterActions.CurrentAction != null) return;

        if (!_isCombatMode)
        {
            _currentCombatStyleExpertise = null;
            ApplyCivilAnimator();
            return;
        }

        WeaponInstance weapon = _character.CharacterEquipment.CurrentWeapon;
        WeaponType type = WeaponType.Barehands;

        if (weapon != null && weapon.ItemSO is WeaponSO weaponData)
        {
            type = weaponData.WeaponType;
        }

        if (_selectedStyles.TryGetValue(type, out var expertise))
        {
            _currentCombatStyleExpertise = expertise;
            
            var anim = _character.CharacterVisual.CharacterAnimator.Animator;
            var targetController = expertise.GetCurrentAnimator();
            
            // --- SÉCURITÉ : On ne change le contrôleur que s'il est différent ---
            // Ré-assigner le même contrôleur force Unity à reset l'Animator (et donc l'animation en cours).
            if (anim.runtimeAnimatorController != targetController)
            {
                anim.runtimeAnimatorController = targetController;
            }
        }
        else
        {
            _currentCombatStyleExpertise = null;
            ApplyCivilAnimator();
        }
    }

    private void ApplyCivilAnimator()
    {
        var animHandler = _character.CharacterVisual.CharacterAnimator;
        var anim = animHandler.Animator;
        RuntimeAnimatorController targetController = null;

        if (animHandler.CivilAnimatorController != null)
        {
            targetController = animHandler.CivilAnimatorController;
        }
        else if (_character.RigType?.baseSpritesLibrary?.DefaultAnimatorController != null)
        {
            targetController = _character.RigType.baseSpritesLibrary.DefaultAnimatorController;
        }

        if (targetController != null && anim.runtimeAnimatorController != targetController)
        {
            anim.runtimeAnimatorController = targetController;
        }
    }

    #region Attack System Methods
    public bool Attack()
    {
        return MeleeAttack();
    }

    private float _lastMeleeAttackCallTime;

    public bool MeleeAttack()
    {
        // S?curit? : Pas plus d'un appel par 0.2s (pour ?viter les doubles clicks/AI transients)
        if (Time.time - _lastMeleeAttackCallTime < 0.2f) return false;
        _lastMeleeAttackCallTime = Time.time;

        _lastCombatActionTime = Time.time;
        if (!_isCombatMode)
        {
            _isCombatMode = true;
            RefreshCurrentAnimator();
        }

        return _character.CharacterActions.ExecuteAction(new CharacterMeleeAttackAction(_character));
    }

    /// <summary>
    /// Appelé via Animation Event au début de la phase active de l'attaque.
    /// Fait apparaître le prefab de style de combat (hitbox).
    /// </summary>
    public void SpawnCombatStyleAttackInstance()
    {
        if (_currentCombatStyleExpertise == null || _currentCombatStyleExpertise.Style == null) return;
        if (_currentCombatStyleExpertise.Style.Prefab == null) return;

        // Positionnement : Extremité de la direction du regard (X) et milieu du sprite (Y)
        // On récupère la direction via le booléen IsFacingRight car le transform ne tourne pas (scale flip)
        Vector3 faceDir = _character.CharacterVisual.IsFacingRight ? Vector3.right : Vector3.left;
        
        // On demande au visuel le point au bord du sprite dans cette direction.
        // En passant une direction purement horizontale, GetVisualExtremity renverra center.y pour la hauteur.
        Vector3 spawnPos = _character.CharacterVisual.GetVisualExtremity(faceDir);
        // On aligne sur l'axe Z r?el du personnage pour ?viter les d?calages de sprites
        spawnPos.z = transform.position.z;
        
        _activeCombatStyleInstance = Instantiate(_currentCombatStyleExpertise.Style.Prefab, spawnPos, transform.rotation, transform);
        _activeCombatStyleInstance.GetComponent<CombatStyleAttack>()?.Initialize(_character, _bonusMeleeMaxTargets);
    }

    /// <summary>
    /// Appelé via Animation Event à la fin de la phase active de l'attaque.
    /// Détruit le prefab de style de combat.
    /// </summary>
    public void DespawnCombatStyleAttackInstance()
    {
        if (_activeCombatStyleInstance != null)
        {
            Destroy(_activeCombatStyleInstance);
            _activeCombatStyleInstance = null;
        }
    }
    #endregion

    #region HP Management
    public void TakeDamage(float amount, MeleeDamageType type = MeleeDamageType.Blunt)
    {
        if (!_character.IsAlive() || _character.Stats == null) return;

        _character.Stats.Health.CurrentAmount -= amount;
        Debug.Log($"<color=green>[Combat]</color> {_character.CharacterName} took {amount} {type} damage.");

        if (_character.CharacterVisual != null && _character.CharacterVisual.CharacterBlink != null)
        {
            _character.CharacterVisual.CharacterBlink.Blink();
        }

        if (_character.Stats.Health.CurrentAmount <= 0)
        {
            _character.SetUnconscious(true);
        }
    }

    public void Heal(float amount)
    {
        if (_character.Stats != null && _character.Stats.Health != null)
        {
            _character.Stats.Health.IncreaseCurrentAmount(amount);
            Debug.Log($"<color=green>[Combat]</color> {_character.CharacterName} a été soigné de {amount} HP.");
        }
    }
    #endregion
}
