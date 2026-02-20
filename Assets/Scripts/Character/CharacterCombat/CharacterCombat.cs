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

    // --- NOUVEAUX CHAMPS DÉPLACÉS ---
    [SerializeField] private BattleManager _currentBattleManager;
    [SerializeField] private GameObject _battleManagerPrefab;

    private Dictionary<WeaponType, CombatStyleExpertise> _selectedStyles = new Dictionary<WeaponType, CombatStyleExpertise>();

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
    public BattleManager BattleManager => _currentBattleManager;

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

        if (target.CharacterCombat.IsInBattle)
        {
            Debug.LogWarning($"<color=yellow>[Battle]</color> {target.CharacterName} est déjà occupé dans un autre combat.");
            return false;
        }

        Debug.Log($"<color=green>[Battle]</color> Validation réussie : Combat entre {_character.CharacterName} et {target.CharacterName} possible.");
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
}
