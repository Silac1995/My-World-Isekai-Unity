using UnityEngine;

/// <summary>
/// Ranged weapon that requires a charge time before firing (e.g. bow).
/// </summary>
[System.Serializable]
public class ChargingWeaponInstance : RangedWeaponInstance
{
    [SerializeField] private float _chargeProgress = 0f;
    [SerializeField] private bool _isCharged = false;

    public ChargingWeaponInstance(ItemSO data) : base(data) { }

    public float ChargeProgress => _chargeProgress;
    public bool IsCharged => _isCharged;

    public override bool CanFire() => _isCharged;

    /// <summary>
    /// Starts or continues charging. Returns true when the charge is complete.
    /// </summary>
    public bool Charge(float deltaTime, float chargingTime)
    {
        if (_isCharged) return true;

        _chargeProgress += deltaTime;
        if (_chargeProgress >= chargingTime)
        {
            _isCharged = true;
            _chargeProgress = chargingTime;
        }
        return _isCharged;
    }

    /// <summary>
    /// Resets the charge after a shot.
    /// </summary>
    public void ResetCharge()
    {
        _chargeProgress = 0f;
        _isCharged = false;
    }
}
