using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: dispatches a consumable's effect via
/// <see cref="ConsumableInstance.ApplyEffect"/>. The item instance is consumed
/// (removed from its source — bag slot or hand carry).
///
/// <para>Sources supported: BagSlot, HandsCarry. Worn slots and active weapon
/// cannot be Use targets (the verb does not appear in their popups).</para>
///
/// <para>Queued server-side: either by the player-UI bridge ServerRpc
/// (<c>CharacterActions.RequestEquipmentVerbServerRpc</c>) or directly by future
/// NPC AI (e.g. <c>GoapAction_BuyFood</c>'s eat step). Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_UseItem : CharacterAction
{
    private readonly EquipmentSourceRef _source;

    public EquipmentSourceRef Source => _source;

    public CharacterAction_UseItem(Character character, EquipmentSourceRef source)
        : base(character, 0f)
    {
        _source = source;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (_source.Kind != EquipmentSourceKind.BagSlot && _source.Kind != EquipmentSourceKind.HandsCarry) return false;
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation v1 — could trigger Trigger_Eat later */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;

        ItemInstance instance = ResolveAndDetach(equip);
        if (instance == null) return;

        if (!(instance is ConsumableInstance consumable))
        {
            if (NPCDebug.VerboseActions)
                Debug.LogWarning($"<color=orange>[UseItem]</color> {character.CharacterName} aborted: {instance.ItemSO.ItemName} is not a ConsumableInstance.");
            return;
        }

        consumable.ApplyEffect(character);
        if (NPCDebug.VerboseActions)
            Debug.Log($"<color=green>[UseItem]</color> {character.CharacterName} used {consumable.ItemSO.ItemName}.");
    }

    private ItemInstance ResolveAndDetach(CharacterEquipment equip)
    {
        switch (_source.Kind)
        {
            case EquipmentSourceKind.BagSlot:
            {
                var inv = equip.GetInventory();
                if (inv == null || _source.BagIndex < 0 || _source.BagIndex >= inv.ItemSlots.Count) return null;
                var slot = inv.ItemSlots[_source.BagIndex];
                if (slot.IsEmpty()) return null;
                ItemInstance item = slot.ItemInstance;
                inv.RemoveItem(item, character);
                return item;
            }
            case EquipmentSourceKind.HandsCarry:
            {
                var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
                if (hands == null || !hands.IsCarrying) return null;
                ItemInstance item = hands.CarriedItem;
                hands.ClearCarriedItem();  // destroys visual without WorldItem spawn
                return item;
            }
            default:
                return null;
        }
    }
}
