[System.Serializable]
public abstract class RangedWeaponInstance : WeaponInstance
{
    protected RangedWeaponInstance(ItemSO data) : base(data) { }

    /// <summary>
    /// Indique si l'arme est prête à tirer.
    /// </summary>
    public abstract bool CanFire();
}
