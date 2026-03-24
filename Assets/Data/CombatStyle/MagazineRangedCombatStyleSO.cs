using UnityEngine;

/// <summary>
/// Style de combat à distance avec chargeur (ex: pistolet, arbalète).
/// L'arme a un nombre limité de munitions et nécessite un rechargement.
/// </summary>
[CreateAssetMenu(fileName = "MagazineRangedStyle", menuName = "Scriptable Objects/Combat Style/Magazine Ranged Style")]
public class MagazineRangedCombatStyleSO : RangedCombatStyleSO
{
    [Header("Magazine Settings")]
    [SerializeField] private int _magazineSize = 6;
    [SerializeField] private float _reloadTime = 2f;

    public int MagazineSize => _magazineSize;
    public float ReloadTime => _reloadTime;

    public override WeaponType WeaponType => WeaponType.None; // À définir selon le type d'arme
}
