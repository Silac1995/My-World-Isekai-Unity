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

    public void CancelCollect()
    {
        _wasCollected = false;
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
        if (WorldItem == null || WorldItem.NetworkObject == null || !WorldItem.NetworkObject.IsSpawned) return;

        WorldItem.RequestInteractServerRpc(interactor.NetworkObject.NetworkObjectId);
    }
}
