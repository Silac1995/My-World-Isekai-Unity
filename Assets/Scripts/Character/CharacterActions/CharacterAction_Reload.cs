/// <summary>
/// STUB — real implementation in Task 5.
/// Reloads a MagazineWeaponInstance over the weapon's reload duration.
/// </summary>
public sealed class CharacterAction_Reload : CharacterAction
{
    private readonly MagazineWeaponInstance _mag;

    public CharacterAction_Reload(Character character, MagazineWeaponInstance mag)
        : base(character)
    {
        _mag = mag;
    }

    public override void OnStart() { }
    public override void OnApplyEffect() { }
}
