public abstract class CharacterNeed
{
    protected Character _character;

    public CharacterNeed(Character character)
    {
        _character = character;
    }

    // Le besoin est-il actif ? (Ex: IsNaked() == true)
    public abstract bool IsActive();

    // Quelle est l'urgence ? (0 = rien, 100 = vital)
    public abstract float GetUrgency();

    // Quelle action ou behaviour ce besoin doit-il déclencher ?
    public abstract void Resolve(NPCController npc);
}