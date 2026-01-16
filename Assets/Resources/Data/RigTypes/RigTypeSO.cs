using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RigType", menuName = "Scriptable Objects/Rig Type")]
public class RigTypeSO : ScriptableObject
{
    [SerializeField] public BaseSpritesLibrarySO baseSpritesLibrary;

    [Header("Available Combat Styles")]
    [SerializeField] private List<CombatStyleSO> _availableStyles = new List<CombatStyleSO>();

    // Permet de filtrer les styles par type d'arme pour ce Rig
    public List<CombatStyleSO> GetStylesForWeapon(WeaponType weaponType)
    {
        return _availableStyles.FindAll(style => style.WeaponType == weaponType);
    }
}