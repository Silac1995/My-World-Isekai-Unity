[System.Serializable]
public abstract class RangedWeaponInstance : WeaponInstance
{
    protected RangedWeaponInstance(ItemSO data) : base(data) { }

    /// <summary>
    /// Indicates whether the weapon is ready to fire.
    /// </summary>
    public abstract bool CanFire();
}
