using UnityEngine;

/// <summary>
/// Arme à distance avec chargeur (ex: pistolet, arbalète).
/// Nombre limité de munitions, nécessite un rechargement.
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
    /// Consomme une munition. Retourne false si le chargeur est vide.
    /// </summary>
    public bool ConsumeAmmo()
    {
        if (_currentAmmo <= 0) return false;
        _currentAmmo--;
        return true;
    }

    /// <summary>
    /// Commence le rechargement.
    /// </summary>
    public void StartReload()
    {
        _isReloading = true;
    }

    /// <summary>
    /// Termine le rechargement (appelé après le timer de reload).
    /// </summary>
    public void FinishReload()
    {
        _currentAmmo = _magazineSize;
        _isReloading = false;
    }
}
