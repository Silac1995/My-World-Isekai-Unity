# Furniture Placement HUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow characters (player and NPC) to carry furniture as items, place them anywhere via ghost-based HUD (player) or AI (NPC), and pick up installed furniture back into carried form.

**Architecture:** New `FurnitureItemSO`/`FurnitureItemInstance` data types for the portable form. Updated `CharacterPlaceFurnitureAction` and new `CharacterPickUpFurnitureAction` as shared actions for both player and NPC. `FurniturePlacementManager` as a player-only UI layer that queues the placement action. New methods on `FurnitureManager` for registering/unregistering already-spawned networked furniture.

**Tech Stack:** Unity 2022+, Netcode for GameObjects, C#

**Spec:** `docs/superpowers/specs/2026-03-29-furniture-placement-hud-design.md`

---

## Task 1: FurnitureItemSO and FurnitureItemInstance

**Files:**
- Create: `Assets/Resources/Data/Item/FurnitureItemSO.cs`
- Create: `Assets/Scripts/Item/FurnitureItemInstance.cs`

These are the data types for the portable furniture form. Follow the existing `MiscSO`/`MiscInstance` pattern exactly.

- [ ] **Step 1: Create `FurnitureItemInstance.cs`**

```csharp
// Assets/Scripts/Item/FurnitureItemInstance.cs
[System.Serializable]
public class FurnitureItemInstance : ItemInstance
{
    public FurnitureItemInstance(ItemSO data) : base(data) { }
}
```

- [ ] **Step 2: Create `FurnitureItemSO.cs`**

```csharp
// Assets/Resources/Data/Item/FurnitureItemSO.cs
using UnityEngine;

[CreateAssetMenu(fileName = "New Furniture Item", menuName = "Scriptable Objects/Items/Furniture")]
public class FurnitureItemSO : ItemSO
{
    [Header("Furniture")]
    [Tooltip("The prefab instantiated when this furniture is placed in the world.")]
    [SerializeField] private Furniture _installedFurniturePrefab;

    public Furniture InstalledFurniturePrefab => _installedFurniturePrefab;

    public override System.Type InstanceType => typeof(FurnitureItemInstance);

    public override ItemInstance CreateInstance()
    {
        return new FurnitureItemInstance(this);
    }
}
```

- [ ] **Step 3: Refresh assets in Unity**

Run via MCP: `assets-refresh` to trigger compilation. Check `console-get-logs` for errors.

- [ ] **Step 4: Verify in Unity Editor**

Right-click in Project → Create → Scriptable Objects → Items → Furniture. Confirm the asset creates successfully and shows the `_installedFurniturePrefab` field.

- [ ] **Step 5: Commit**

```
feat: add FurnitureItemSO and FurnitureItemInstance data types
```

---

## Task 2: Add `_furnitureItemSO` back-reference to Furniture.cs

**Files:**
- Modify: `Assets/Scripts/World/Furniture/Furniture.cs:10-14`

The bidirectional link: `FurnitureItemSO` → Furniture prefab, and `Furniture` → back to `FurnitureItemSO`.

- [ ] **Step 1: Add field and property to `Furniture.cs`**

After the existing `_sizeInCells` field (line 14), add:

```csharp
[Header("Item Data")]
[Tooltip("The FurnitureItemSO this furniture converts back to when picked up. Leave empty for non-pickable furniture.")]
[SerializeField] private FurnitureItemSO _furnitureItemSO;

public FurnitureItemSO FurnitureItemSO => _furnitureItemSO;
```

- [ ] **Step 2: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 3: Commit**

```
feat: add FurnitureItemSO back-reference to Furniture
```

---

## Task 3: Add `RegisterSpawnedFurniture` and `UnregisterAndRemove` to FurnitureManager

**Files:**
- Modify: `Assets/Scripts/World/Buildings/FurnitureManager.cs:114-128`

New methods for networked furniture that is already instantiated and spawned by the server. The existing `AddFurniture()` (which instantiates) and `RemoveFurniture()` (which destroys) remain untouched for NPC/legacy use.

- [ ] **Step 1: Add `RegisterSpawnedFurniture` method**

Add after the existing `RemoveFurniture` method (after line 100), before `FindAvailableFurniture`:

```csharp
/// <summary>
/// Registers an already-instantiated and network-spawned Furniture onto the grid and list.
/// Does NOT instantiate — the caller is responsible for spawning.
/// Used by CharacterPlaceFurnitureAction for networked furniture.
/// </summary>
public bool RegisterSpawnedFurniture(Furniture furniture, Vector3 targetPosition)
{
    if (_grid == null || furniture == null) return false;
    if (!_grid.CanPlaceFurniture(targetPosition, furniture.SizeInCells)) return false;

    _grid.RegisterFurniture(furniture, targetPosition, furniture.SizeInCells);
    _furnitures.Add(furniture);
    furniture.transform.SetParent(transform);

    string roomName = _room != null ? _room.RoomName : gameObject.name;
    Debug.Log($"<color=green>[FurnitureManager]</color> Registered spawned {furniture.FurnitureName} at {targetPosition} in {roomName}.");
    return true;
}
```

- [ ] **Step 2: Add `UnregisterAndRemove` method**

Add directly after `RegisterSpawnedFurniture`:

```csharp
/// <summary>
/// Unregisters furniture from grid and list without destroying the GameObject.
/// Caller handles destruction/despawn (e.g. NetworkObject.Despawn).
/// Used by CharacterPickUpFurnitureAction for networked furniture.
/// </summary>
public void UnregisterAndRemove(Furniture furniture)
{
    if (furniture == null) return;
    if (_grid != null) _grid.UnregisterFurniture(furniture);
    _furnitures.Remove(furniture);

    string roomName = _room != null ? _room.RoomName : gameObject.name;
    Debug.Log($"<color=cyan>[FurnitureManager]</color> Unregistered {furniture.FurnitureName} from {roomName}.");
}
```

- [ ] **Step 3: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 4: Commit**

```
feat: add RegisterSpawnedFurniture and UnregisterAndRemove to FurnitureManager
```

---

## Task 4: Update `CharacterPlaceFurnitureAction`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterPlaceFurnitureAction.cs`

Update the existing action to support:
1. A new player constructor that takes `FurnitureItemSO` + position + rotation (item consumed from hands)
2. Network-aware spawning via `NetworkObject.Spawn()`
3. Freestanding placement outside rooms
4. Grid registration via `FurnitureManager.RegisterSpawnedFurniture()` when inside a room

The existing NPC constructors (room + prefab) remain functional.

- [ ] **Step 1: Rewrite the full file**

```csharp
// Assets/Scripts/Character/CharacterActions/CharacterPlaceFurnitureAction.cs
using UnityEngine;
using Unity.Netcode;

public class CharacterPlaceFurnitureAction : CharacterAction
{
    private Room _targetRoom;
    private Furniture _furniturePrefab;
    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private bool _hasTargetPosition;
    private FurnitureItemSO _furnitureItemSO;
    private bool _consumeFromHands;

    /// <summary>
    /// Player path: position chosen by HUD, item consumed from hands.
    /// Works both inside and outside rooms.
    /// </summary>
    public CharacterPlaceFurnitureAction(Character character, FurnitureItemSO furnitureItemSO, Vector3 targetPosition, Quaternion rotation, float duration = 1.0f)
        : base(character, duration)
    {
        _furnitureItemSO = furnitureItemSO;
        _furniturePrefab = furnitureItemSO.InstalledFurniturePrefab;
        _targetPosition = targetPosition;
        _targetRotation = rotation;
        _hasTargetPosition = true;
        _consumeFromHands = true;
        _targetRoom = FindRoomAtPosition(targetPosition);
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
    /// NPC path: room + explicit target position.
    /// </summary>
    public CharacterPlaceFurnitureAction(Character character, Room room, Furniture furniturePrefab, Vector3 targetPosition, float duration = 1.0f)
        : base(character, duration)
    {
        _targetRoom = room;
        _furniturePrefab = furniturePrefab;
        _targetPosition = targetPosition;
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
                _hasTargetPosition = true;
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Action]</color> No free position for {_furniturePrefab.FurnitureName} in {_targetRoom.RoomName}.");
                return false;
            }
        }

        // If inside a room, validate grid placement
        if (_targetRoom != null && _targetRoom.FurnitureManager != null)
        {
            if (!_targetRoom.FurnitureManager.IsPlacementValid(_furniturePrefab, _targetPosition))
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

        // Server-only: instantiate and spawn the networked furniture
        // OnApplyEffect runs on both server and client, but only the server can Spawn NetworkObjects
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            Furniture placed = Object.Instantiate(_furniturePrefab, _targetPosition, _targetRotation);

            var netObj = placed.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }

            // Register with room grid if inside a room
            if (_targetRoom != null && _targetRoom.FurnitureManager != null)
            {
                _targetRoom.FurnitureManager.RegisterSpawnedFurniture(placed, _targetPosition);
            }

            Debug.Log($"<color=green>[Action]</color> {_furniturePrefab.FurnitureName} placed at {_targetPosition}.");
        }

        // Consume item from hands — runs on owner (client-authoritative hands via ClientNetworkTransform)
        if (_consumeFromHands)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.IsCarrying)
            {
                hands.DropCarriedItem(); // Removes from hands (item consumed, not dropped to world)
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
```

- [ ] **Step 2: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 3: Commit**

```
feat: update CharacterPlaceFurnitureAction with player path, network spawn, freestanding support
```

---

## Task 5: Create `CharacterPickUpFurnitureAction`

**Files:**
- Create: `Assets/Scripts/Character/CharacterActions/CharacterPickUpFurnitureAction.cs`

Shared action for both players (via interaction menu) and NPCs (via AI). Mirrors `CharacterPickUpItem` pattern.

- [ ] **Step 1: Create the action file**

```csharp
// Assets/Scripts/Character/CharacterActions/CharacterPickUpFurnitureAction.cs
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

        // Must have a FurnitureItemSO to convert back to
        if (_targetFurniture.FurnitureItemSO == null)
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {_targetFurniture.FurnitureName} has no FurnitureItemSO — cannot be picked up.");
            return false;
        }

        // Can't pick up furniture someone is using
        if (_targetFurniture.IsOccupied)
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {_targetFurniture.FurnitureName} is occupied by {_targetFurniture.Occupant.CharacterName}.");
            return false;
        }

        // Hands must be free
        var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands == null || !hands.AreHandsFree())
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName}'s hands are not free.");
            return false;
        }

        // Proximity check
        float dist = Vector3.Distance(character.transform.position, _targetFurniture.transform.position);
        if (dist > 3f)
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName} is too far from {_targetFurniture.FurnitureName} ({dist:F1}m).");
            return false;
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

        // Create the portable item instance
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
            // Edge case: hands became occupied between CanExecute and OnApplyEffect
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
```

- [ ] **Step 2: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 3: Commit**

```
feat: add CharacterPickUpFurnitureAction for player and NPC furniture pickup
```

---

## Task 6: Add "Pick Up" option to FurnitureInteractable

**Files:**
- Modify: `Assets/Scripts/Interactable/FurnitureInteractable.cs`

Add a "Pick Up" hold-interaction option. When the player holds E on a furniture, they get a menu with "Pick Up" (and future options like "Use"). On selection, queues `CharacterPickUpFurnitureAction`.

- [ ] **Step 1: Add `GetHoldInteractionOptions` override**

Add the following method and required `using` to `FurnitureInteractable.cs`:

Add at the top of the file:
```csharp
using System.Collections.Generic;
```

Add after the `Release()` method, before the closing brace:

```csharp
public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
{
    var options = new List<InteractionOption>();

    // "Pick Up" option — only for furniture that has a FurnitureItemSO assigned
    if (_furniture != null && _furniture.FurnitureItemSO != null)
    {
        bool isDisabled = _furniture.IsOccupied;
        var hands = interactor.CharacterVisual?.BodyPartsController?.HandsController;
        if (hands != null && !hands.AreHandsFree()) isDisabled = true;

        options.Add(new InteractionOption
        {
            Name = "Pick Up",
            IsDisabled = isDisabled,
            Action = () =>
            {
                var action = new CharacterPickUpFurnitureAction(interactor, _furniture);
                interactor.CharacterActions.ExecuteAction(action);
            }
        });
    }

    return options.Count > 0 ? options : null;
}
```

- [ ] **Step 2: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 3: Commit**

```
feat: add Pick Up hold-interaction to FurnitureInteractable
```

---

## Task 7: Create `FurniturePlacementManager`

**Files:**
- Create: `Assets/Scripts/World/Buildings/FurniturePlacementManager.cs`

Player-only UI layer: ghost visual, mouse-based positioning, validation. On confirm, queues `CharacterPlaceFurnitureAction` — never spawns furniture directly.

- [ ] **Step 1: Create the file**

```csharp
// Assets/Scripts/World/Buildings/FurniturePlacementManager.cs
using UnityEngine;
using Unity.Netcode;
using MWI.UI.Notifications;

public class FurniturePlacementManager : CharacterSystem
{
    [Header("Settings")]
    [SerializeField] private LayerMask _groundLayer;
    [SerializeField] private LayerMask _obstacleLayer;
    [SerializeField] private KeyCode _placementKey = KeyCode.F;
    [SerializeField] private float _maxPlacementRange = 10f;
    [SerializeField] private Material _ghostMaterialValid;
    [SerializeField] private Material _ghostMaterialInvalid;

    [Header("Notifications")]
    [SerializeField] private ToastNotificationChannel _toastChannel;

    private GameObject _ghostInstance;
    private FurnitureItemSO _activeFurnitureItemSO;
    private Furniture _ghostFurnitureComponent;
    private bool _isPlacementActive;
    private bool _isDebugMode;
    private Quaternion _ghostRotation = Quaternion.identity;

    public bool IsPlacementActive => _isPlacementActive;

    // ────────────────────── Entry Points ──────────────────────

    /// <summary>
    /// Debug mode: enter placement without carrying the item. Called by DebugScript.
    /// </summary>
    public void StartPlacementDebug(FurnitureItemSO furnitureItemSO)
    {
        if (furnitureItemSO == null || furnitureItemSO.InstalledFurniturePrefab == null)
        {
            Debug.LogError("[FurniturePlacementManager] Invalid FurnitureItemSO or missing installed prefab.");
            return;
        }

        _isDebugMode = true;
        StartPlacement(furnitureItemSO);
    }

    private void StartPlacement(FurnitureItemSO furnitureItemSO)
    {
        ClearGhost();

        _activeFurnitureItemSO = furnitureItemSO;
        _ghostInstance = Instantiate(furnitureItemSO.InstalledFurniturePrefab.gameObject);
        _ghostFurnitureComponent = _ghostInstance.GetComponent<Furniture>();
        _ghostRotation = Quaternion.identity;

        // Disable physics/logic on ghost
        if (_ghostInstance.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
        foreach (var col in _ghostInstance.GetComponentsInChildren<Collider>()) col.enabled = false;
        if (_ghostInstance.TryGetComponent(out NetworkObject netObj)) netObj.enabled = false;

        // Set layer to Ignore Raycast so it doesn't block ground raycast or push characters
        SetLayerRecursive(_ghostInstance, LayerMask.NameToLayer("Ignore Raycast"));

        _ghostInstance.name = "FurnitureGhost_" + furnitureItemSO.name;
        _isPlacementActive = true;

        if (_character != null && !_character.IsBuilding)
            _character.SetBuildingState(true);

        ApplyGhostMaterials(_ghostMaterialValid);
    }

    public void CancelPlacement()
    {
        ClearGhost();
        if (_character != null)
            _character.SetBuildingState(false);
    }

    private void ClearGhost()
    {
        if (_ghostInstance != null)
        {
            Destroy(_ghostInstance);
            _ghostInstance = null;
        }
        _isPlacementActive = false;
        _activeFurnitureItemSO = null;
        _ghostFurnitureComponent = null;
        _isDebugMode = false;
    }

    // ────────────────────── CharacterSystem Overrides ──────────────────────

    protected override void HandleIncapacitated(Character character)
    {
        base.HandleIncapacitated(character);
        CancelPlacement();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        base.HandleCombatStateChanged(inCombat);
        if (inCombat) CancelPlacement();
    }

    private void OnDestroy()
    {
        CancelPlacement();
    }

    // ────────────────────── Frame Update (Player only) ──────────────────────

    private void Update()
    {
        if (!IsOwner) return;

        // Check for placement key press while carrying furniture
        if (!_isPlacementActive && Input.GetKeyDown(_placementKey))
        {
            var hands = _character?.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.CarriedItem is FurnitureItemInstance furnitureItem)
            {
                var furnitureItemSO = furnitureItem.ItemSO as FurnitureItemSO;
                if (furnitureItemSO != null)
                {
                    StartPlacement(furnitureItemSO);
                }
            }
        }

        if (!_isPlacementActive) return;

        UpdateGhostPosition();
        HandleRotationInput();
        HandlePlacementInput();
    }

    private void UpdateGhostPosition()
    {
        if (_ghostInstance == null || Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, _groundLayer))
        {
            _ghostInstance.transform.position = hit.point;
            _ghostInstance.transform.rotation = _ghostRotation;

            bool isValid = ValidatePlacement(hit.point);
            ApplyGhostMaterials(isValid ? _ghostMaterialValid : _ghostMaterialInvalid);
        }
    }

    private void HandleRotationInput()
    {
        if (Input.GetKeyDown(KeyCode.Q))
            _ghostRotation *= Quaternion.Euler(0, -90f, 0);
        if (Input.GetKeyDown(KeyCode.E))
            _ghostRotation *= Quaternion.Euler(0, 90f, 0);
    }

    private void HandlePlacementInput()
    {
        // Left-click: confirm placement
        if (Input.GetMouseButtonDown(0))
        {
            if (_ghostInstance != null && ValidatePlacement(_ghostInstance.transform.position))
            {
                ConfirmPlacement(_ghostInstance.transform.position, _ghostRotation);
            }
        }

        // Right-click: cancel current ghost but keep building mode
        if (Input.GetMouseButtonDown(1))
        {
            ClearGhost();
        }

        // Escape: exit placement mode completely
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelPlacement();
        }
    }

    private void ConfirmPlacement(Vector3 position, Quaternion rotation)
    {
        if (_activeFurnitureItemSO == null) return;

        // Queue the shared CharacterAction
        var action = new CharacterPlaceFurnitureAction(
            _character,
            _activeFurnitureItemSO,
            position,
            rotation
        );

        if (_character.CharacterActions.ExecuteAction(action))
        {
            ClearGhost();

            // In debug mode, re-enter placement immediately for rapid testing
            if (_isDebugMode)
            {
                // Don't clear building state — stay in placement mode
                return;
            }

            // Normal mode: exit building state
            if (_character != null)
                _character.SetBuildingState(false);
        }
    }

    // ────────────────────── Validation ──────────────────────

    public bool ValidatePlacement(Vector3 position)
    {
        if (_character == null || _ghostFurnitureComponent == null) return false;

        // Range check
        float dist = Vector3.Distance(_character.transform.position, position);
        if (dist > _maxPlacementRange) return false;

        // Obstacle overlap (ghost colliders are disabled, so it won't detect itself)
        BoxCollider ghostBox = _ghostInstance.GetComponent<BoxCollider>();
        if (ghostBox != null)
        {
            Vector3 center = _ghostInstance.transform.TransformPoint(ghostBox.center);
            Vector3 halfExtents = Vector3.Scale(ghostBox.size, _ghostInstance.transform.lossyScale) * 0.45f;
            Collider[] overlaps = Physics.OverlapBox(center, halfExtents, _ghostInstance.transform.rotation, _obstacleLayer);
            if (overlaps.Length > 0) return false;
        }

        // Grid check if inside a room
        Room room = FindRoomAtPosition(position);
        if (room != null && room.Grid != null)
        {
            if (!room.Grid.CanPlaceFurniture(position, _ghostFurnitureComponent.SizeInCells))
                return false;
        }

        return true;
    }

    // ────────────────────── Helpers ──────────────────────

    private Room FindRoomAtPosition(Vector3 position)
    {
        Room[] allRooms = FindObjectsByType<Room>(FindObjectsSortMode.None);
        foreach (var room in allRooms)
        {
            if (room.IsPointInsideRoom(position)) return room;
        }
        return null;
    }

    private void ApplyGhostMaterials(Material mat)
    {
        if (mat == null || _ghostInstance == null) return;
        foreach (var renderer in _ghostInstance.GetComponentsInChildren<Renderer>())
        {
            renderer.sharedMaterial = mat; // Use sharedMaterial to avoid material instance leaks
        }
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }
}
```

- [ ] **Step 2: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 3: Commit**

```
feat: add FurniturePlacementManager (player-only ghost HUD)
```

---

## Task 8: Register `FurniturePlacementManager` on Character

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs:68-74` (fields), `:164-169` (properties), `:321-335` (Awake)

Follow the existing pattern for adding a new CharacterSystem reference.

- [ ] **Step 1: Add SerializeField**

After line 73 (`[SerializeField] private CharacterParty _characterParty;`), before `#endregion`, add:

```csharp
[SerializeField] private FurniturePlacementManager _furniturePlacementManager;
```

- [ ] **Step 2: Add public property**

After the existing `PlacementManager` property (line 169), add:

```csharp
public FurniturePlacementManager FurniturePlacementManager => _furniturePlacementManager;
```

- [ ] **Step 3: Add Awake auto-assignment**

After line 335 (`if (_characterParty == null) _characterParty = GetComponentInChildren<CharacterParty>();`), add:

```csharp
if (_furniturePlacementManager == null) _furniturePlacementManager = GetComponentInChildren<FurniturePlacementManager>();
```

- [ ] **Step 4: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 5: Commit**

```
feat: expose FurniturePlacementManager on Character facade
```

---

## Task 9: Update DebugScript

**Files:**
- Modify: `Assets/Scripts/DebugScript.cs`

Change the test furniture button to use the new placement flow instead of auto-placing.

- [ ] **Step 1: Change the field type**

Replace line 23:
```csharp
[SerializeField] private Furniture _testFurniturePrefab;
```
With:
```csharp
[SerializeField] private FurnitureItemSO _testFurnitureItemSO;
```

- [ ] **Step 2: Replace `TestInstallFurniture` method**

Replace the entire `TestInstallFurniture()` method (lines 169-253) with:

```csharp
private void TestInstallFurniture()
{
    Character player = FindObjectOfType<PlayerController>()?.GetComponent<Character>();
    if (player == null)
    {
        Debug.LogWarning("[Debug] No player found for furniture test.");
        return;
    }

    if (_testFurnitureItemSO == null)
    {
        Debug.LogError("[Debug] No _testFurnitureItemSO assigned in DebugScript inspector.");
        return;
    }

    if (player.FurniturePlacementManager == null)
    {
        Debug.LogError("[Debug] Player has no FurniturePlacementManager. Add it as a child CharacterSystem.");
        return;
    }

    player.FurniturePlacementManager.StartPlacementDebug(_testFurnitureItemSO);
    Debug.Log($"<color=green>[Debug]</color> Started furniture placement mode for {_testFurnitureItemSO.name}.");
}
```

- [ ] **Step 3: Refresh and verify compilation**

Run via MCP: `assets-refresh`. Check `console-get-logs` for errors.

- [ ] **Step 4: Commit**

```
feat: update DebugScript to use FurniturePlacementManager for furniture testing
```

---

## Task 10: Update Building System SKILL.md

**Files:**
- Modify: `.agent/skills/building_system/SKILL.md`

Document the new furniture placement flow, data types, and actions.

- [ ] **Step 1: Add Furniture Placement section**

After the existing "## The Furniture System" section, add a new section documenting:
- `FurnitureItemSO` / `FurnitureItemInstance` data types and lifecycle
- `CharacterPlaceFurnitureAction` updated constructors (player vs NPC paths)
- `CharacterPickUpFurnitureAction` and its flow
- `FurniturePlacementManager` as player-only UI layer
- `FurnitureManager.RegisterSpawnedFurniture()` and `UnregisterAndRemove()`
- The "Pick Up" hold-interaction on `FurnitureInteractable`

- [ ] **Step 2: Commit**

```
docs: update building system SKILL.md with furniture placement flow
```

---

## Task 11: Integration Testing

**Files:** None (manual verification in Unity Editor)

- [ ] **Step 1: Create a test FurnitureItemSO asset**

In Unity: Right-click → Create → Scriptable Objects → Items → Furniture. Name it "Test Chair". Assign an existing Furniture prefab as `_installedFurniturePrefab`. Set up icon and WorldItemPrefab (FurnitureCrate prefab).

- [ ] **Step 2: Set up the bidirectional link**

On the Furniture prefab, assign the new FurnitureItemSO to `_furnitureItemSO`. Add `FurnitureInteractable` component if not present.

- [ ] **Step 3: Add FurniturePlacementManager to Character prefab**

Create a child GameObject on the Character prefab, add `FurniturePlacementManager`. Configure `_groundLayer`, `_obstacleLayer`, ghost materials. Assign it on `Character._furniturePlacementManager`.

- [ ] **Step 4: Test debug placement**

Assign the test FurnitureItemSO to DebugScript. Enter Play mode. Click "Test Install Furniture". Verify ghost appears, follows mouse, green/red validation works, Q/E rotation works. Left-click to place. Verify furniture spawns.

- [ ] **Step 5: Test normal placement (carry → place)**

Spawn a FurnitureCrate WorldItem. Pick it up (should go in hands). Press F. Verify ghost placement mode starts. Place the furniture. Verify item removed from hands.

- [ ] **Step 6: Test pickup**

Walk to the placed furniture. Hold E. Verify "Pick Up" option appears. Select it. Verify furniture is removed from world and appears in hands as crate.

- [ ] **Step 7: Test room grid registration**

Place furniture inside a room. Verify grid cells turn red (occupied) in Scene view Gizmos. Pick up the furniture. Verify grid cells turn green (free).

- [ ] **Step 8: Test freestanding placement**

Place furniture outside any room. Verify it spawns without grid errors.
