using UnityEngine;
using Unity.Netcode;

public class CharacterPlaceFurnitureAction : CharacterAction
{
    private Room _targetRoom;
    private Furniture _furniturePrefab;
    private Vector3 _targetPosition;      // Where the furniture GameObject is instantiated (visual center)
    private Vector3 _gridAnchorPosition;  // The bottom-left cell position for grid registration
    private Quaternion _targetRotation;
    private bool _hasTargetPosition;
    private FurnitureItemSO _furnitureItemSO;
    private bool _consumeFromHands;

    /// <summary>
    /// Player path: visual position + grid anchor chosen by HUD, item consumed from hands.
    /// Works both inside and outside rooms.
    /// </summary>
    public CharacterPlaceFurnitureAction(Character character, FurnitureItemSO furnitureItemSO, Vector3 visualPosition, Vector3 gridAnchor, Quaternion rotation, float duration = 1.0f)
        : base(character, duration)
    {
        _furnitureItemSO = furnitureItemSO;
        _furniturePrefab = furnitureItemSO.InstalledFurniturePrefab;
        _targetPosition = visualPosition;
        _gridAnchorPosition = gridAnchor;
        _targetRotation = rotation;
        _hasTargetPosition = true;
        _consumeFromHands = true;
        _targetRoom = FindRoomAtPosition(gridAnchor);
    }

    /// <summary>
    /// NPC path: room + auto-find closest free position.
    /// </summary>
    public CharacterPlaceFurnitureAction(Character character, Room room, Furniture furniturePrefab, float duration = 1.0f)
        : base(character, duration)
    {
        _targetRoom = room;
        _furniturePrefab = furniturePrefab;
        _targetRotation = Quaternion.identity;
        _hasTargetPosition = false;
        _consumeFromHands = false;
    }

    /// <summary>
    /// NPC path: room + explicit target position (anchor = visual for NPC, single cell).
    /// </summary>
    public CharacterPlaceFurnitureAction(Character character, Room room, Furniture furniturePrefab, Vector3 targetPosition, float duration = 1.0f)
        : base(character, duration)
    {
        _targetRoom = room;
        _furniturePrefab = furniturePrefab;
        _targetPosition = targetPosition;
        _gridAnchorPosition = targetPosition;
        _targetRotation = Quaternion.identity;
        _hasTargetPosition = true;
        _consumeFromHands = false;
    }

    public override bool CanExecute()
    {
        if (_furniturePrefab == null) return false;

        // Player path: must be carrying the furniture item
        if (_consumeFromHands)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands == null || !(hands.CarriedItem is FurnitureItemInstance))
            {
                Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName} is not carrying a furniture item.");
                return false;
            }
        }

        // NPC path without target: find closest free position
        if (!_hasTargetPosition)
        {
            if (_targetRoom == null) return false;
            FurnitureGrid grid = _targetRoom.Grid;
            if (grid == null) return false;

            if (grid.GetClosestFreePosition(character.transform.position, _furniturePrefab.SizeInCells, out Vector3 bestPos))
            {
                _targetPosition = bestPos;
                _gridAnchorPosition = bestPos;
                _hasTargetPosition = true;
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Action]</color> No free position for {_furniturePrefab.FurnitureName} in {_targetRoom.RoomName}.");
                return false;
            }
        }

        // If inside a room, validate grid placement using the anchor position
        if (_targetRoom != null && _targetRoom.FurnitureManager != null)
        {
            if (!_targetRoom.FurnitureManager.IsPlacementValid(_furniturePrefab, _gridAnchorPosition))
                return false;
        }

        return true;
    }

    public override void OnStart()
    {
        character.CharacterVisual?.FaceTarget(_targetPosition);
        Debug.Log($"<color=cyan>[Action]</color> {character.CharacterName} is placing {_furniturePrefab.FurnitureName}.");
    }

    public override void OnApplyEffect()
    {
        if (_furniturePrefab == null) return;

        // Request server to spawn the furniture (works from both host and client)
        if (_furnitureItemSO != null && character.CharacterActions != null)
        {
            character.CharacterActions.RequestFurniturePlaceServerRpc(
                _furnitureItemSO.ItemId,
                _targetPosition,
                _gridAnchorPosition,
                _targetRotation
            );
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            // NPC path (no FurnitureItemSO): direct server spawn
            Furniture placed = Object.Instantiate(_furniturePrefab, _targetPosition, _targetRotation);

            var netObj = placed.GetComponent<NetworkObject>();
            if (netObj != null)
                netObj.Spawn();

            if (_targetRoom != null && _targetRoom.FurnitureManager != null)
                _targetRoom.FurnitureManager.RegisterSpawnedFurniture(placed, _gridAnchorPosition);

            Debug.Log($"<color=green>[Action]</color> {_furniturePrefab.FurnitureName} placed at {_targetPosition}.");
        }

        // Consume item from hands — runs on owner
        if (_consumeFromHands)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.IsCarrying)
            {
                hands.DropCarriedItem();
            }
        }
    }

    private Room FindRoomAtPosition(Vector3 position)
    {
        Room[] allRooms = Object.FindObjectsByType<Room>(FindObjectsSortMode.None);
        foreach (var room in allRooms)
        {
            if (room.IsPointInsideRoom(position)) return room;
        }
        return null;
    }
}
