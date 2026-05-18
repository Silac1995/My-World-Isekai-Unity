using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: moves an item from its source (hand carry, worn slot,
/// or active weapon) into the bag's first compatible free slot. Falls back to a
/// ground drop only when the bag has no compatible space.
///
/// <para>Sources supported:</para>
/// <list type="bullet">
///   <item><b>HandsCarry</b> — the hand-carry item (any kind). Hand becomes free.</item>
///   <item><b>WornSlot</b> — routes through <see cref="CharacterEquipment.UnequipToBag"/>.</item>
///   <item><b>ActiveWeapon</b> — wields off via <see cref="CharacterEquipment.WieldOffToHand"/>,
///   then stashes the detached weapon into the bag (or ground on full).</item>
/// </list>
///
/// <para>BagSlot is not a valid source (you can't stash a bagged item into the bag).</para>
///
/// <para>Queued server-side: either by the player-UI bridge ServerRpc
/// (<c>CharacterActions.RequestEquipmentVerbServerRpc</c>) or directly by future
/// NPC AI. Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_StashInBag : CharacterAction
{
    private readonly EquipmentSourceRef _source;

    public EquipmentSourceRef Source => _source;

    public CharacterAction_StashInBag(Character character, EquipmentSourceRef source)
        : base(character, 0f)
    {
        _source = source;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (_source.Kind == EquipmentSourceKind.BagSlot) return false; // bag → bag is invalid
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;

        switch (_source.Kind)
        {
            case EquipmentSourceKind.HandsCarry:
                StashFromHands(equip);
                break;
            case EquipmentSourceKind.WornSlot:
                equip.UnequipToBag(_source.Layer, _source.Slot);
                break;
            case EquipmentSourceKind.ActiveWeapon:
                StashFromActiveWeapon(equip);
                break;
            default:
                Debug.LogWarning($"<color=orange>[StashInBag]</color> unsupported source kind {_source.Kind}.");
                break;
        }
    }

    private void StashFromHands(CharacterEquipment equip)
    {
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.IsCarrying) return;

        ItemInstance carried = hands.CarriedItem;

        // Try bag first; only drop to ground on failure.
        var inv = equip.GetInventory();
        if (inv != null && inv.HasFreeSpaceForItem(carried))
        {
            hands.DropCarriedItem();        // clears hand (no WorldItem spawn)
            inv.AddItem(carried, character);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[StashInBag]</color> {character.CharacterName} stashed {carried.ItemSO.ItemName} from hand → bag.");
        }
        else
        {
            // Bag full — drop to world via the existing physical-drop helper.
            hands.DropCarriedItem();
            CharacterDropItem.ExecutePhysicalDrop(character, carried);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[StashInBag]</color> {character.CharacterName} bag full → dropped {carried.ItemSO.ItemName} to ground.");
        }
    }

    private void StashFromActiveWeapon(CharacterEquipment equip)
    {
        WeaponInstance detached = equip.WieldOffToHand();
        if (detached == null) return;

        var inv = equip.GetInventory();
        if (inv != null && inv.HasFreeSpaceForItem(detached))
        {
            inv.AddItem(detached, character);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=cyan>[StashInBag]</color> {character.CharacterName} stashed active weapon {detached.ItemSO.ItemName} → bag.");
        }
        else
        {
            CharacterDropItem.ExecutePhysicalDrop(character, detached);
            if (NPCDebug.VerboseActions)
                Debug.Log($"<color=orange>[StashInBag]</color> {character.CharacterName} bag full → dropped active weapon {detached.ItemSO.ItemName} to ground.");
        }
    }
}
