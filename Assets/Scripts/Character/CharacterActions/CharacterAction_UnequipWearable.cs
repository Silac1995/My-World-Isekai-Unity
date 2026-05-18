using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative: removes a wearable from the specified worn layer/slot
/// and routes it through <see cref="CharacterEquipment.UnequipToBag"/> — which
/// stashes to the bag first and only drops to ground when the bag is full.
///
/// <para>Queued server-side: either by the player-UI bridge ServerRpc
/// (<c>CharacterActions.RequestEquipmentVerbServerRpc</c>) or directly by future
/// NPC AI. Rule #22 player↔NPC parity.</para>
/// </summary>
public sealed class CharacterAction_UnequipWearable : CharacterAction
{
    private readonly WearableLayerEnum _layer;
    private readonly WearableType _slot;

    public WearableLayerEnum Layer => _layer;
    public WearableType Slot => _slot;

    public CharacterAction_UnequipWearable(Character character, WearableLayerEnum layer, WearableType slot)
        : base(character, 0f)
    {
        _layer = layer;
        _slot = slot;
    }

    public override bool CanExecute()
    {
        if (character == null) return false;
        return character.CharacterEquipment != null;
    }

    public override void OnStart() { /* no animation */ }

    public override void OnApplyEffect()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (character == null) return;

        var equip = character.CharacterEquipment;
        if (equip == null) return;

        // Returns true on bag-stash, false on ground-drop. Either outcome is "successful
        // unequip" from the UI's perspective; we only log the path for diagnostics.
        bool stashed = equip.UnequipToBag(_layer, _slot);
        if (NPCDebug.VerboseActions)
            Debug.Log($"<color=cyan>[UnequipWearable]</color> {character.CharacterName} unequipped {_layer}/{_slot} → {(stashed ? "bag" : "ground")}.");
    }
}
