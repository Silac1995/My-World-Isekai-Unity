public interface ICharacterInteractionAction
{
    /// <summary>
    /// Executes the interaction between the source character and the target character.
    /// </summary>
    void Execute(Character source, Character target);
}
