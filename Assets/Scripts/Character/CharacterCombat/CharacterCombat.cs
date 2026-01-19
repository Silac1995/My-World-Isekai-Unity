using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CharacterCombat : MonoBehaviour
{
    [SerializeField] private Character _character;

    [Header("Expertise & Memory")]
    [SerializeField] private List<CombatStyleExpertise> _knownStyles = new List<CombatStyleExpertise>();

    // --- NOUVEAUX CHAMPS DÉPLACÉS ---
    [SerializeField] private BattleManager _currentBattleManager; // Type changé en BattleManager
    [SerializeField] private GameObject _battleManagerPrefab;

    private Dictionary<WeaponType, CombatStyleExpertise> _selectedStyles = new Dictionary<WeaponType, CombatStyleExpertise>();

    #region Battle Logic
    // Propriété corrigée pour correspondre au type
    public bool IsInBattle => _currentBattleManager != null;
    public BattleManager BattleManager => _currentBattleManager;

    public void JoinBattle(BattleManager manager) => _currentBattleManager = manager;
    public void LeaveBattle() => _currentBattleManager = null;

    public void StartFight(Character target)
    {
        if (!ValidateFight(target)) return;

        // 1. Instanciation du prefab
        GameObject instanceGo = Instantiate(_battleManagerPrefab);
        BattleManager manager = instanceGo.GetComponent<BattleManager>();

        if (manager == null)
        {
            Debug.LogError("<color=red>[Battle]</color> Le prefab n'a pas le script BattleManager !");
            Destroy(instanceGo);
            return;
        }

        // 2. Assigne le script BattleManager (et non le GameObject)
        this._currentBattleManager = manager;

        // 3. Initialisation (Le manager s'occupera d'appeler JoinBattle sur la cible)
        manager.Initialize(_character, target);

        Debug.Log($"<color=orange>[Battle]</color> {_character.CharacterName} a provoqué {target.CharacterName} !");
    }

    private bool ValidateFight(Character target)
    {
        // 1. Vérification de la cible nulle
        if (target == null)
        {
            Debug.LogWarning("<color=red>[Battle]</color> Impossible de combattre : La cible est null.");
            return false;
        }

        // 2. Vérification de l'état de vie
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

        // 3. Vérification de l'état de combat (pour éviter les doublons de BattleManager)
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

        // Si tout est OK
        Debug.Log($"<color=green>[Battle]</color> Validation réussie : Combat entre {_character.CharacterName} et {target.CharacterName} possible.");
        return true;
    }
    #endregion


    /// <summary>
    /// Sélectionne et SAUVEGARDE un style pour un type d'arme.
    /// </summary>
    public void SelectStyle(CombatStyleSO styleToSelect)
    {
        CombatStyleExpertise expertise = _knownStyles.FirstOrDefault(e => e.Style == styleToSelect);

        if (expertise != null)
        {
            // On enregistre le choix pour ce type d'arme (ex: Sword -> Style B)
            _selectedStyles[styleToSelect.WeaponType] = expertise;

            // On rafraîchit l'animator immédiatement au cas où on tient l'arme en main
            RefreshCurrentAnimator();

            Debug.Log($"<color=green>[Combat]</color> Style {styleToSelect.StyleName} sauvegardé pour {styleToSelect.WeaponType}");
        }
    }

    /// <summary>
    /// Point d'entrée lors d'un changement d'arme.
    /// </summary>
    public void OnWeaponChanged(WeaponInstance weapon)
    {
        if (weapon == null || weapon.ItemSO is not WeaponSO weaponData)
        {
            ApplyCivilAnimator();
            return;
        }

        WeaponType type = weaponData.WeaponType;

        // 1. On vérifie si on a déjà un choix sauvegardé pour cette arme
        if (!_selectedStyles.ContainsKey(type))
        {
            // 2. Si c'est la première fois, on fait une sélection automatique initiale
            AutoSelectInitialStyle(type);
        }

        // 3. On applique l'animator basé sur ce qui est dans le dictionnaire
        RefreshCurrentAnimator();
    }

    private void AutoSelectInitialStyle(WeaponType type)
    {
        // On prend le premier style connu pour cette arme
        var firstMatch = _knownStyles.FirstOrDefault(e => e.GetWeaponType() == type);
        if (firstMatch != null)
        {
            _selectedStyles[type] = firstMatch;
        }
    }

    public void RefreshCurrentAnimator()
    {
        // Si character.CharacterEquipment._weapon est null, weaponData sera null
        WeaponInstance weapon = _character.CharacterEquipment.CurrentWeapon;

        if (weapon == null || weapon.ItemSO is not WeaponSO weaponData)
        {
            ApplyCivilAnimator(); // Le perso est "nu" ou mains nues
            return;
        }

        // Ici, on utilise la sauvegarde du dictionnaire.
        // Si tu as sélectionné "Style B" pour l'épée, c'est ce qui ressortira, 
        // même après avoir utilisé une lance entre temps.
        if (_selectedStyles.TryGetValue(weaponData.WeaponType, out var expertise))
        {
            _character.CharacterVisual.CharacterAnimator.Animator.runtimeAnimatorController = expertise.GetCurrentAnimator();
        }
        else
        {
            ApplyCivilAnimator();
        }
    }

    private void ApplyCivilAnimator()
    {
        if (_character.RigType?.baseSpritesLibrary?.DefaultAnimatorController != null)
        {
            _character.CharacterVisual.CharacterAnimator.Animator.runtimeAnimatorController =
                _character.RigType.baseSpritesLibrary.DefaultAnimatorController;
        }
    }
}