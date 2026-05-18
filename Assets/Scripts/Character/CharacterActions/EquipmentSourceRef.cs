/// <summary>
/// Discriminator for the four places an item can live on a Character that the
/// equipment-window CharacterAction surface needs to reference uniformly:
/// a bag slot, a worn layer slot, the active weapon slot, or the hands-carry slot.
/// </summary>
public enum EquipmentSourceKind
{
    BagSlot      = 0,
    WornSlot     = 1,
    ActiveWeapon = 2,
    HandsCarry   = 3,
}

/// <summary>
/// Tiny serializable discriminated value identifying the source of an item for
/// equipment-window actions (CarryInHand, StashInBag, UseItem). Read-only.
///
/// <para>BagIndex is meaningful only when Kind == BagSlot.
/// Layer + Slot are meaningful only when Kind == WornSlot.
/// ActiveWeapon and HandsCarry need no payload (one slot each).</para>
///
/// <para>Construct via the static factories (Bag / Worn / Weapon / Hands) for
/// clarity at call sites.</para>
/// </summary>
[System.Serializable]
public readonly struct EquipmentSourceRef
{
    public readonly EquipmentSourceKind Kind;
    public readonly int BagIndex;
    public readonly WearableLayerEnum Layer;
    public readonly WearableType Slot;

    public EquipmentSourceRef(
        EquipmentSourceKind kind,
        int bagIndex = -1,
        WearableLayerEnum layer = WearableLayerEnum.Underwear,
        WearableType slot = WearableType.Helmet)
    {
        Kind = kind;
        BagIndex = bagIndex;
        Layer = layer;
        Slot = slot;
    }

    public static EquipmentSourceRef Bag(int index) =>
        new EquipmentSourceRef(EquipmentSourceKind.BagSlot, bagIndex: index);

    public static EquipmentSourceRef Worn(WearableLayerEnum layer, WearableType slot) =>
        new EquipmentSourceRef(EquipmentSourceKind.WornSlot, layer: layer, slot: slot);

    public static EquipmentSourceRef Weapon() =>
        new EquipmentSourceRef(EquipmentSourceKind.ActiveWeapon);

    public static EquipmentSourceRef Hands() =>
        new EquipmentSourceRef(EquipmentSourceKind.HandsCarry);

    public override string ToString() => Kind switch
    {
        EquipmentSourceKind.BagSlot      => $"Bag[{BagIndex}]",
        EquipmentSourceKind.WornSlot     => $"Worn[{Layer}/{Slot}]",
        EquipmentSourceKind.ActiveWeapon => "ActiveWeapon",
        EquipmentSourceKind.HandsCarry   => "HandsCarry",
        _ => $"<unknown:{Kind}>",
    };
}
