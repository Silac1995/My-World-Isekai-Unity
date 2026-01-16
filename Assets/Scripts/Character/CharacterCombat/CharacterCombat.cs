using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CharacterCombat : MonoBehaviour
{
    [SerializeField] private Character _character;

    [Header("Expertise & Memory")]
    [SerializeField] private List<CombatStyleExpertise> _knownStyles = new List<CombatStyleExpertise>();

    // Ce dictionnaire sauvegarde le DERNIER style sélectionné pour CHAQUE type d'arme.
    // Il ne sera pas vidé lors d'un switch d'arme.
    private Dictionary<WeaponType, CombatStyleExpertise> _selectedStyles = new Dictionary<WeaponType, CombatStyleExpertise>();

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
        WeaponInstance weapon = _character.CharacterEquipment.CurrentWeapon;

        if (weapon == null || weapon.ItemSO is not WeaponSO weaponData)
        {
            ApplyCivilAnimator();
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