using UnityEngine;

/// <summary>
/// Ranged weapon with a magazine (e.g. pistol, crossbow).
/// Limited ammo count, requires reloading.
/// </summary>
[System.Serializable]
public class MagazineWeaponInstance : RangedWeaponInstance
{
    [SerializeField] private int _currentAmmo;
    [SerializeField] private int _magazineSize;
    [SerializeField] private bool _isReloading = false;

    public MagazineWeaponInstance(ItemSO data) : base(data)
    {
        if (data is WeaponSO weaponData)
        {
            _magazineSize = weaponData.MagazineSize;
            _currentAmmo = _magazineSize;
        }
    }

    public int CurrentAmmo => _currentAmmo;
    public int MagazineSize => _magazineSize;
    public bool IsReloading => _isReloading;
    public bool IsEmpty => _currentAmmo <= 0;

    public override bool CanFire() => _currentAmmo > 0 && !_isReloading;

    /// <summary>
    /// Consumes one ammo. Returns false if the magazine is empty.
    /// </summary>
    public bool ConsumeAmmo()
    {
        if (_currentAmmo <= 0) return false;
        _currentAmmo--;
        return true;
    }

    /// <summary>
    /// Starts reloading.
    /// </summary>
    public void StartReload()
    {
        _isReloading = true;
    }

    /// <summary>
    /// Finishes reloading (called after the reload timer expires).
    /// </summary>
    public void FinishReload()
    {
        _currentAmmo = _magazineSize;
        _isReloading = false;
    }

    /// <summary>
    /// Aborts an in-progress reload without changing the magazine state.
    /// Called by CharacterAction_Reload.OnInterrupt when a reload is cancelled
    /// (knockback, death, swap-during-reload). Leaves CurrentAmmo at its pre-reload value.
    /// </summary>
    public void CancelReload()
    {
        _isReloading = false;
    }
}
