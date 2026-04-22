using UnityEngine;
using UnityEngine.TextCore.Text;

public class CharacterDropItem : CharacterAction
{
    private ItemInstance _itemInstance;

    public CharacterDropItem(Character character, ItemInstance item) : base(character, 0.5f)
    {
        _itemInstance = item ?? throw new System.ArgumentNullException(nameof(item));
    }

    public override void OnStart()
    {
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null) animator.SetTrigger("Trigger_Drop");

        Debug.Log($"{character.CharacterName} prepare le drop.");
    }

    public override void OnApplyEffect()
    {
        bool removed = false;

        var equip = character.CharacterEquipment;
        if (equip != null && equip.HaveInventory())
        {
            if (equip.GetInventory().RemoveItem(_itemInstance, character))
            {
                removed = true;
            }
        }

        if (!removed)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.CarriedItem == _itemInstance)
            {
                hands.DropCarriedItem();
                removed = true;
            }
        }

        if (removed)
        {
            ExecutePhysicalDrop(character, _itemInstance);
        }
    }

    /// <summary>
    /// Helper statique pour forcer un drop physique immédiat sans passer par l'Animator.
    /// Utile lors de la mort, de l'incapacitation ou de l'entrée en combat.
    /// </summary>
    public static void ExecutePhysicalDrop(Character owner, ItemInstance item)
    {
        if (owner == null || item == null) return;

        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            // Server: spawn directly
            Vector3 dropPos = owner.transform.position + Vector3.up * 1.5f;
            Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            WorldItem.SpawnWorldItem(item, dropPos + offset);

            Debug.Log($"<color=cyan>[CharacterDropItem]</color> {item.ItemSO.ItemName} dropped in world (server).");
        }
        else if (owner.CharacterActions != null)
        {
            // Client: request server to spawn the dropped item
            string jsonData = JsonUtility.ToJson(item);
            owner.CharacterActions.RequestItemDropServerRpc(
                item.ItemSO.ItemId,
                jsonData,
                owner.transform.position
            );
            Debug.Log($"<color=cyan>[CharacterDropItem]</color> {item.ItemSO.ItemName} drop requested from client.");
        }
    }
}
