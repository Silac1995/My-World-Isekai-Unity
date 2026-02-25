using UnityEngine;

public class ItemInteractable : InteractableObject
{
    [SerializeField] private WorldItem _worldItem;

    private bool _wasCollected = false;

    public bool TryCollect()
    {
        if (_wasCollected) return false;
        _wasCollected = true;
        return true;
    }

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
            if (WorldItem == null) return null;
            return WorldItem.ItemInstance;
        }
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null) return;

        var instance = ItemInstance;
        if (instance == null)
        {
            Debug.LogWarning("[Interaction] Pas d'instance. Objet deja ramasse.");
            return;
        }

        GameObject rootToDestroy = RootGameObject;

        // A. EQUIPEMENT PORTABLE (Vetements, sacs...)
        if (instance is WearableInstance wearable)
        {
            CharacterEquipAction equipAction = new CharacterEquipAction(interactor, wearable);
            interactor.CharacterActions.ExecuteAction(equipAction);
            if (rootToDestroy != null) Object.Destroy(rootToDestroy);
            Debug.Log("[Equip] " + wearable.CustomizedName + " porte.");
        }
        // B. ARME OU OBJET DIVERS (On ramasse dans inventaire)
        else
        {
            CharacterPickUpItem pickUpAction = new CharacterPickUpItem(interactor, instance, rootToDestroy);
            interactor.CharacterActions.ExecuteAction(pickUpAction);
        }
    }
}
