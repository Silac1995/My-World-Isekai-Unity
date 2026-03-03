using UnityEngine;

/// <summary>
/// Arme à distance qui nécessite un temps de charge avant de tirer (ex: arc).
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
    /// Démarre ou continue la charge. Retourne true quand la charge est complète.
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
    /// Réinitialise la charge après un tir.
    /// </summary>
    public void ResetCharge()
    {
        _chargeProgress = 0f;
        _isCharged = false;
    }
}
