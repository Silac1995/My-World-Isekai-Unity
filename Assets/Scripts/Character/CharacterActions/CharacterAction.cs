public abstract class CharacterAction
{
    protected Character character;

    protected CharacterAction(Character character)
    {
        this.character = character ?? throw new System.ArgumentNullException(nameof(character));
    }

    public abstract void PerformAction();
}