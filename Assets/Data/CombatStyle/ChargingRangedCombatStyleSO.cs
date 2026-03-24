using UnityEngine;

/// <summary>
/// Style de combat à distance avec charge (ex: arc).
/// Le personnage doit charger pendant _chargingTime avant de pouvoir tirer.
/// </summary>
[CreateAssetMenu(fileName = "ChargingRangedStyle", menuName = "Scriptable Objects/Combat Style/Charging Ranged Style")]
public class ChargingRangedCombatStyleSO : RangedCombatStyleSO
{
    [Header("Charging Settings")]
    [SerializeField] private float _chargingTime = 1.5f;

    public float ChargingTime => _chargingTime;

    public override WeaponType WeaponType => WeaponType.Bow;
}
