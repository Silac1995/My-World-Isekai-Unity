using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Grants the receiver a pre-configured ItemInstance via CharacterEquipment.PickUpItem.
    /// Server-side; relies on existing inventory replication.
    ///
    /// Design note: Items in this project have no shared SO factory — each item type
    /// (WeaponSO, WearableSO, MiscSO, etc.) creates its own ItemInstance subclass.
    /// This reward therefore holds a pre-built ItemInstance directly (serialized as a
    /// [SerializeReference] field so Unity can serialize concrete subclasses).
    ///
    /// A simpler v1 alternative: leave the item slot as a TODO and wire it in the
    /// Inspector once the item system provides a factory or template mechanism.
    ///
    /// TODO(orders): Replace the [SerializeReference] pattern with a proper ItemTemplate
    /// SO once an ItemFactory / ItemSO.CreateInstance() pathway is established.
    /// Tracked in wiki/projects/optimisation-backlog.md.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Rewards/Give Item", fileName = "Reward_GiveItem_New")]
    public class Reward_GiveItem : ScriptableObject, IOrderReward
    {
        [Tooltip("Pre-built item instance to grant. Assign in Inspector. This uses SerializeReference to support any ItemInstance subclass.")]
        [SerializeReference] private ItemInstance _item;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (receiver == null) return;

            if (_item == null)
            {
                Debug.LogWarning($"<color=yellow>[Reward_GiveItem]</color> No item configured on SO '{name}'; skipping.");
                return;
            }

            if (receiver.CharacterEquipment == null)
            {
                Debug.LogWarning($"<color=yellow>[Reward_GiveItem]</color> {receiver.CharacterName} has no CharacterEquipment; skipping.");
                return;
            }

            try
            {
                bool added = receiver.CharacterEquipment.PickUpItem(_item);
                if (added)
                {
                    Debug.Log($"<color=green>[Order]</color> {receiver.CharacterName} received item '{_item.CustomizedName}' as compliance reward.");
                }
                else
                {
                    Debug.LogWarning($"<color=yellow>[Reward_GiveItem]</color> {receiver.CharacterName} could not pick up '{_item.CustomizedName}' (inventory full or incompatible slot).");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
