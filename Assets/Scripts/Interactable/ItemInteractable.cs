using NUnit.Framework.Internal.Execution;
using UnityEngine;

public class ItemInteractable : InteractableObject
{
    [SerializeField] private WorldItem _worldItem;

    // On sécurise l'accès au WorldItem
    public WorldItem WorldItem
    {
        get
        {
            if (_worldItem == null) _worldItem = GetComponentInParent<WorldItem>();
            return _worldItem;
        }
    }

    public ItemInstance ItemInstance
    {
        get
        {
            // Si on ne trouve pas le WorldItem, on renvoie null proprement au lieu de spammer l'erreur fatale tout de suite
            if (WorldItem == null) return null;
            return WorldItem.ItemInstance;
        }
    }

    public override void Interact(Character interactor)
    {
        var instance = ItemInstance;
        if (instance == null)
        {
            Debug.LogWarning($"[Interaction] Pas d'instance sur {name}. L'objet est peut-être déjà ramassé.");
            return;
        }
        Debug.Log($"[INTERACT] Objet : {instance.ItemSO.ItemName} | Type : {instance.GetType().Name}");
    }
}