using UnityEngine;

/// <summary>
/// Charging ranged combat style (e.g. bow).
/// The character must charge for _chargingTime before being able to fire.
/// </summary>
[CreateAssetMenu(fileName = "ChargingRangedStyle", menuName = "Scriptable Objects/Combat Style/Charging Ranged Style")]
public class ChargingRangedCombatStyleSO : RangedCombatStyleSO
{
    [Header("Charging Settings")]
    [SerializeField] private float _chargingTime = 1.5f;

    public float ChargingTime => _chargingTime;

    public override WeaponType WeaponType => WeaponType.Bow;
}
