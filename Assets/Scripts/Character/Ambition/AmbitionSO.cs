namespace MWI.Ambition
{
    /// <summary>
    /// Discriminator for AmbitionContext entries. The save layer routes serialization
    /// per kind: Character → CharacterId UUID, Primitive → string-encoded value,
    /// Enum → string-encoded name, ItemSO/AmbitionSO/QuestSO/NeedSO → asset GUID,
    /// Zone → IWorldZone GUID.
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
