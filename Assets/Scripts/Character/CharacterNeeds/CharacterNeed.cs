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
    // Retourne true si une action a été effectivement lancée.
    public abstract bool Resolve(NPCController npc);
}
