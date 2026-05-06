namespace MWI.Ambition
{
    /// <summary>
    /// Discriminator for AmbitionContext entries. See spec — drives save layer routing.
    /// </summary>
    public enum ContextValueKind
    {
        Character,
        Primitive,
        Enum,
        ItemSO,
        AmbitionSO,
        QuestSO,
        NeedSO,
        Zone
    }
}
