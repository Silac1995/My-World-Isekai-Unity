using UnityEngine;
using Unity.Netcode;

public class CharacterPickUpItem : CharacterAction
{
    private ItemInstance _item;
    private GameObject _worldObject;

    public CharacterPickUpItem(Character character, ItemInstance item, GameObject worldObject) : base(character, 3f)
    {
        _item = item;
        _worldObject = worldObject;

        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            float duration = animHandler.GetCachedDuration("Female_Humanoid_Pickup_from_ground_00");
            if (duration > 0)
            {
                this.Duration = duration;
            }
        }
    }

    public override void OnStart()
    {
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler?.Animator != null)
        {
            animHandler.Animator.SetTrigger(CharacterAnimator.ActionTrigger);
        }

        if (_worldObject != null)
        {
            var rb = _worldObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            var worldItem = _worldObject.GetComponent<WorldItem>();
            if (worldItem != null) worldItem.IsBeingCarried = true;
        }
    }

    public override void OnApplyEffect()
    {
        if (character.CharacterEquipment != null && character.CharacterEquipment.PickUpItem(_item))
        {
            if (_worldObject != null)
            {
                var netObj = _worldObject.GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                    netObj.Despawn(true);
                else
                    Object.Destroy(_worldObject);
            }
        }
        else
        {
            Debug.LogWarning($"[Action] Pickup failed for {_item.CustomizedName}. Item remains on ground.");
        }
    }

    public override bool CanExecute()
    {
        if (character.CharacterEquipment == null) return false;

        if (!character.CharacterEquipment.CanCarryItemAnyMore(_item))
        {
            Debug.LogWarning($"[Action] {character.CharacterName} cannot carry {_item.CustomizedName} (Inventory full or No Bag).");
            return false;
        }

        // Si l'objet possède une zone d'interaction formelle, s'assurer qu'on y est.
        if (_worldObject != null)
        {
            var interactable = _worldObject.GetComponent<InteractableObject>() ?? _worldObject.GetComponentInParent<InteractableObject>();
            if (interactable != null && interactable.InteractionZone != null)
            {
                var zoneBounds = interactable.InteractionZone.bounds;
                var charCollider = character.GetComponent<Collider>();

                if (charCollider != null)
                {
                    bool intersects = zoneBounds.Intersects(charCollider.bounds) || zoneBounds.Contains(character.transform.position);
                    if (!intersects)
                    {
                        Vector3 closestPoint = zoneBounds.ClosestPoint(character.transform.position);
                        closestPoint.y = 0;
                        Vector3 charPos = character.transform.position;
                        charPos.y = 0;

                        if (Vector3.Distance(charPos, closestPoint) > 1.5f)
                        {
                            Debug.Log($"[Action] {character.CharacterName} est hors de la zone d'interaction de {_item.CustomizedName}.");
                            return false;
                        }
                    }
                }
                else
                {
                    Vector3 closestPoint = zoneBounds.ClosestPoint(character.transform.position);
                    closestPoint.y = 0;
                    Vector3 charPos = character.transform.position;
                    charPos.y = 0;

                    if (Vector3.Distance(charPos, closestPoint) > 1.5f)
                    {
                        Debug.Log($"[Action] {character.CharacterName} est trop loin de {_item.CustomizedName} pour le ramasser.");
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
