using UnityEngine;
using Unity.Netcode;

public class CharacterPickUpFurnitureAction : CharacterAction
{
    private Furniture _targetFurniture;

    public CharacterPickUpFurnitureAction(Character character, Furniture targetFurniture, float duration = 1.5f)
        : base(character, duration)
    {
        _targetFurniture = targetFurniture;

        // Try to get actual animation duration
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            float d = animHandler.GetCachedDuration("Female_Humanoid_Pickup_from_ground_00");
            if (d > 0) Duration = d;
        }
    }

    public override bool CanExecute()
    {
        if (_targetFurniture == null)
        {
            Debug.LogWarning("<color=orange>[Action]</color> Target furniture is null.");
            return false;
        }

        if (_targetFurniture.FurnitureItemSO == null)
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {_targetFurniture.FurnitureName} has no FurnitureItemSO — cannot be picked up.");
            return false;
        }

        if (_targetFurniture.IsOccupied)
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {_targetFurniture.FurnitureName} is occupied by {_targetFurniture.Occupant.CharacterName}.");
            return false;
        }

        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree())
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName}'s hands are not free.");
            return false;
        }

        // Proximity check using the furniture's collider (wraps the whole furniture)
        // rather than transform.position (pivot may be offset)
        Collider furnitureCol = _targetFurniture.GetComponent<Collider>();
        if (furnitureCol != null)
        {
            Vector3 closest = furnitureCol.ClosestPoint(character.transform.position);
            float dist = Vector3.Distance(character.transform.position, closest);
            if (dist > 3f)
            {
                Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName} is too far from {_targetFurniture.FurnitureName} ({dist:F1}m).");
                return false;
            }
        }

        return true;
    }

    public override void OnStart()
    {
        character.CharacterVisual?.FaceTarget(_targetFurniture.transform.position);

        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler?.Animator != null)
        {
            animHandler.Animator.SetTrigger(CharacterAnimator.ActionTrigger);
        }

        Debug.Log($"<color=cyan>[Action]</color> {character.CharacterName} is picking up {_targetFurniture.FurnitureName}.");
    }

    public override void OnApplyEffect()
    {
        if (_targetFurniture == null) return;

        FurnitureItemSO itemSO = _targetFurniture.FurnitureItemSO;
        if (itemSO == null) return;

        FurnitureItemInstance instance = itemSO.CreateInstance() as FurnitureItemInstance;
        if (instance == null)
        {
            Debug.LogError($"<color=red>[Action]</color> Failed to create FurnitureItemInstance from {itemSO.name}.");
            return;
        }

        // Put in character's hands — runs on owner
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree())
        {
            CharacterDropItem.ExecutePhysicalDrop(character, instance, false);
            Debug.LogWarning($"<color=orange>[Action]</color> Hands no longer free. Dropped {itemSO.ItemName} on ground.");
        }
        else
        {
            hands.CarryItem(instance);
        }

        // Server-only: unregister from grid and despawn the NetworkObject
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            Room parentRoom = _targetFurniture.GetComponentInParent<Room>();
            if (parentRoom != null && parentRoom.FurnitureManager != null)
            {
                parentRoom.FurnitureManager.UnregisterAndRemove(_targetFurniture);
            }

            var netObj = _targetFurniture.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
            else
            {
                Object.Destroy(_targetFurniture.gameObject);
            }
        }

        Debug.Log($"<color=green>[Action]</color> {character.CharacterName} picked up {itemSO.ItemName}.");
    }
}
