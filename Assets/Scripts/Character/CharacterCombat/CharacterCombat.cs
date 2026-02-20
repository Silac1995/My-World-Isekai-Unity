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

    // --- NOUVEAUX CHAMPS DÉPLACÉS ---
    [SerializeField] private BattleManager _currentBattleManager;
    [SerializeField] private GameObject _battleManagerPrefab;

    private Dictionary<WeaponType, CombatStyleExpertise> _selectedStyles = new Dictionary<WeaponType, CombatStyleExpertise>();
    private GameObject _activeCombatStyleInstance;

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

    public void JoinBattle(BattleManager manager)
    {
        _currentBattleManager = manager;
        _isCombatMode = true;
        RefreshCurrentAnimator();
    }

    public void LeaveBattle()
    {
        _currentBattleManager = null;
        _isCombatMode = false;
        RefreshCurrentAnimator();
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
        _isCombatMode = !_isCombatMode;
        RefreshCurrentAnimator();
        Debug.Log($"<color=cyan>[Combat]</color> Mode Combat : {(_isCombatMode ? "ACTIVÉ" : "DÉSACTIVÉ")}");
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
            _character.CharacterVisual.CharacterAnimator.Animator.runtimeAnimatorController = expertise.GetCurrentAnimator();
        }
        else
        {
            _currentCombatStyleExpertise = null;
            ApplyCivilAnimator();
        }
    }

    private void ApplyCivilAnimator()
    {
        var anim = _character.CharacterVisual.CharacterAnimator;
        if (anim.CivilAnimatorController != null)
        {
            anim.Animator.runtimeAnimatorController = anim.CivilAnimatorController;
            return;
        }

        if (_character.RigType?.baseSpritesLibrary?.DefaultAnimatorController != null)
        {
            _character.CharacterVisual.CharacterAnimator.Animator.runtimeAnimatorController =
                _character.RigType.baseSpritesLibrary.DefaultAnimatorController;
        }
    }

    #region Attack System Methods
    public void Attack()
    {
        MeleeAttack();
    }

    public void MeleeAttack()
    {
        if (!_isCombatMode)
        {
            _isCombatMode = true;
            RefreshCurrentAnimator();
        }

        _character.CharacterVisual.CharacterAnimator.PlayMeleeAttack();
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
    public void TakeDamage(float amount)
    {
        _character.TakeDamage(amount);
    }

    public void Heal(float amount)
    {
        if (_character.Stats != null && _character.Stats.Health != null)
        {
            _character.Stats.Health.GainCurrentAmount(amount);
            Debug.Log($"<color=green>[Combat]</color> {_character.CharacterName} a été soigné de {amount} HP.");
        }
    }
    #endregion
}
