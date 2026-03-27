/// <summary>
/// Interface for objects that can teach abilities to characters (mentors, books, scrolls, etc.).
/// </summary>
public interface IAbilitySource
{
    AbilitySO GetAbility();
    bool CanLearnFrom(Character learner);
}
