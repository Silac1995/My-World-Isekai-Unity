using UnityEngine;

/// <summary>
/// Magazine-fed ranged combat style (e.g. pistol, crossbow).
/// The weapon has a limited number of rounds and requires reloading.
/// </summary>
[CreateAssetMenu(fileName = "MagazineRangedStyle", menuName = "Scriptable Objects/Combat Style/Magazine Ranged Style")]
public class MagazineRangedCombatStyleSO : RangedCombatStyleSO
{
    [Header("Magazine Settings")]
    [SerializeField] private int _magazineSize = 6;
    [SerializeField] private float _reloadTime = 2f;

    public int MagazineSize => _magazineSize;
    public float ReloadTime => _reloadTime;

    public override WeaponType WeaponType => WeaponType.None; // To be set based on weapon type
}
