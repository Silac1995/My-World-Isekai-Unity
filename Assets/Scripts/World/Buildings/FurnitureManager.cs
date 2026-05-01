using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(FurnitureGrid))]
public class FurnitureManager : MonoBehaviour
{
    [Header("Manager Info")]
    [SerializeField] protected List<Furniture> _furnitures = new List<Furniture>();
    
    private FurnitureGrid _grid;
    private Room _room; // Reference to the parent room for logs and Transform parenting

    public IReadOnlyList<Furniture> Furnitures => _furnitures;
    public FurnitureGrid Grid => _grid;

    // Cached resolution of the owning CommercialBuilding so cache invalidation hooks
    // (Tier 2 D + A) don't pay GetComponentInParent on every register/unregister.
    // Resolved lazily on first mutation; the building hierarchy may not be fully wired
    // at Awake time on some spawn paths.
    private CommercialBuilding _ownerBuilding;
    private bool _ownerBuildingResolved;

    private void Awake()
    {
        _grid = GetComponent<FurnitureGrid>();
        _room = GetComponent<Room>();
    }

    /// <summary>
    /// Invalidate Tier 2 furniture-related caches on the owning CommercialBuilding
    /// when this room's furniture set changes. See wiki/projects/optimisation-backlog.md
    /// entry #2 / D + A. Safe to call before the building is spawned — silently no-op
    /// when no CommercialBuilding ancestor exists (rooms in non-commercial buildings).
    /// </summary>
    private void InvalidateOwnerBuildingCaches()
    {
        if (!_ownerBuildingResolved)
        {
            _ownerBuilding = GetComponentInParent<CommercialBuilding>();
            _ownerBuildingResolved = true;
        }
        if (_ownerBuilding == null) return;
        _ownerBuilding.InvalidateStorageFurnitureCache();
        if (_ownerBuilding is CraftingBuilding crafting)
        {
            crafting.InvalidateCraftableCache();
        }
    }

    /// <summary>
    /// Used to check whether a UI or an NPC can place this furniture at this exact location.
    /// Returns true if the slot is valid on the grid.
    /// </summary>
    public bool IsPlacementValid(Furniture furniturePrefab, Vector3 targetPosition)
    {
        if (_grid == null || furniturePrefab == null) return false;
        return _grid.CanPlaceFurniture(targetPosition, furniturePrefab.SizeInCells);
    }

    /// <summary>
    /// Attempts to add a piece of furniture to this room via the manager.
    /// </summary>
    public bool AddFurniture(Furniture furniturePrefab, Vector3 targetPosition)
    {
        if (_grid == null) return false;

        if (IsPlacementValid(furniturePrefab, targetPosition))
        {
            Furniture newFurniture = Instantiate(furniturePrefab, targetPosition, Quaternion.identity, transform);
            
            // Pivot adjustment: targetPosition is the center of the FIRST cell (bottom-left).
            // If the furniture spans 3x2 cells, the overall visual center of the furniture must be shifted
            // to sit in the middle of those 3x2 cells, instead of being centered on the 1st cell only.
            Renderer[] renderers = newFurniture.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                // The total space reserved on the grid forms a large rectangle.
                // Compute the EXACT center of that large rectangle.
                Vector3 regionCenter = targetPosition + new Vector3(
                    (furniturePrefab.SizeInCells.x - 1) * _grid.CellSize / 2f,
                    0,
                    (furniturePrefab.SizeInCells.y - 1) * _grid.CellSize / 2f
                );

                // Compute the distance between the current 3D-furniture center and the desired grid center
                float offsetX = regionCenter.x - bounds.center.x;
                float offsetZ = regionCenter.z - bounds.center.z;

                // For the height (Y), make sure the lowest point of the mesh touches the floor
                float offsetY = targetPosition.y - bounds.min.y;

                newFurniture.transform.position += new Vector3(offsetX, offsetY, offsetZ);
            }

            _furnitures.Add(newFurniture);
            _grid.RegisterFurniture(newFurniture, targetPosition, newFurniture.SizeInCells);
            InvalidateOwnerBuildingCaches();

            string roomName = _room != null ? _room.RoomName : gameObject.name;
            Debug.Log($"<color=green>[FurnitureManager]</color> Instantiation SUCCESSFUL: {furniturePrefab.name} at {newFurniture.transform.position} in {roomName}!");
            return true;
        }

        string failRoomName = _room != null ? _room.RoomName : gameObject.name;
        Debug.LogWarning($"<color=orange>[FurnitureManager]</color> Invalid or already-occupied slot for furniture {furniturePrefab.FurnitureName} at {targetPosition} in {failRoomName}.");
        return false;
    }

    /// <summary>
    /// Enlève un meuble de cette room.
    /// </summary>
    public void RemoveFurniture(Furniture furnitureToRemove)
    {
        if (_furnitures.Contains(furnitureToRemove))
        {
            _furnitures.Remove(furnitureToRemove);
            if (_grid != null)
            {
                _grid.UnregisterFurniture(furnitureToRemove);
            }
            InvalidateOwnerBuildingCaches();
            Destroy(furnitureToRemove.gameObject);
        }
    }

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
        InvalidateOwnerBuildingCaches();

        string roomName = _room != null ? _room.RoomName : gameObject.name;
        Debug.Log($"<color=green>[FurnitureManager]</color> Registered spawned {furniture.FurnitureName} at {targetPosition} in {roomName}.");
        return true;
    }

    /// <summary>
    /// Server-authored variant of <see cref="RegisterSpawnedFurniture"/> that bypasses the
    /// <c>CanPlaceFurniture</c> validation. Validation exists to gate runtime user input
    /// (player placement); for furniture authored at design time — like <c>Building._defaultFurnitureLayout</c> —
    /// the level designer is the source of truth and the cell at the chosen position is trusted
    /// even if it lies on a wall-marked cell, partially out of bounds, or in any configuration the
    /// validator would otherwise reject. The grid still records the occupancy so subsequent
    /// queries (FindAvailableFurniture, GetCraftableItems) work normally.
    ///
    /// IMPORTANT: this method does **not** SetParent the furniture under the room transform.
    /// The room is a <c>NetworkBehaviour</c> on a non-<c>NetworkObject</c> GameObject (only the
    /// building root carries a NetworkObject). NGO throws <c>InvalidParentException</c> when a
    /// NetworkObject is reparented under a non-NetworkObject. The caller is responsible for
    /// parenting the furniture under a valid NetworkObject ancestor (the building root) before
    /// calling this method. Logical room membership lives in <c>_furnitures</c>, not in transform
    /// parenting — all <c>Room.GetFurniture*</c> queries hit that list, not <c>GetComponentsInChildren</c>.
    /// </summary>
    public bool RegisterSpawnedFurnitureUnchecked(Furniture furniture, Vector3 targetPosition)
    {
        if (_grid == null || furniture == null) return false;

        _grid.RegisterFurniture(furniture, targetPosition, furniture.SizeInCells);
        _furnitures.Add(furniture);
        InvalidateOwnerBuildingCaches();

        string roomName = _room != null ? _room.RoomName : gameObject.name;
        Debug.Log($"<color=green>[FurnitureManager]</color> Registered (unchecked, no reparent) spawned {furniture.FurnitureName} at {targetPosition} in {roomName}.");
        return true;
    }

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
        InvalidateOwnerBuildingCaches();

        string roomName = _room != null ? _room.RoomName : gameObject.name;
        Debug.Log($"<color=cyan>[FurnitureManager]</color> Unregistered {furniture.FurnitureName} from {roomName}.");
    }

    /// <summary>
    /// Trouve un meuble disponible de type T.
    /// </summary>
    public T FindAvailableFurniture<T>() where T : Furniture
    {
        foreach (var f in _furnitures)
        {
            if (f is T typed && !typed.IsOccupied)
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Merges any Furniture currently parented under this room's transform into <see cref="_furnitures"/>
    /// (without wiping prior entries) and registers each on the grid. Includes inactive children so
    /// briefly-disabled networked furniture isn't missed during the spawn cascade.
    ///
    /// IMPORTANT: this method is **additive**, not replace-style. Earlier revisions did
    /// <c>_furnitures = new List&lt;Furniture&gt;(GetComponentsInChildren&lt;Furniture&gt;(true))</c>
    /// — which silently destroyed registrations made via <see cref="RegisterSpawnedFurnitureUnchecked"/>.
    /// That path is the canonical channel for <c>Building._defaultFurnitureLayout</c>:
    /// the spawned furniture is parented under the **building root** (NGO requires a NetworkObject
    /// ancestor; the room sits on a non-NO GameObject so reparenting under it throws
    /// <c>InvalidParentException</c>). Logical room ownership lives in this <c>_furnitures</c>
    /// list, not in transform parenting — so a transform-only rescan can never see those entries
    /// and must not be allowed to clobber them.
    ///
    /// Idempotency: a re-discovered child is skipped (Contains check); the grid registration is
    /// itself idempotent (<see cref="FurnitureGrid.RegisterFurniture"/> just writes Occupant per cell).
    /// Dead references (Unity fake-null after a destroy that bypassed the Remove* helpers) are pruned
    /// up-front so they don't accumulate across <c>OnNetworkSpawn</c> / <c>Start</c> re-runs.
    /// </summary>
    public void LoadExistingFurniture()
    {
        if (_grid == null) return;

        // Drop any Unity fake-null entries left behind by a destroy that didn't go through
        // RemoveFurniture / UnregisterAndRemove.
        _furnitures.RemoveAll(f => f == null);

        Furniture[] childFurniture = GetComponentsInChildren<Furniture>(true);
        bool anyAdded = false;
        foreach (var f in childFurniture)
        {
            if (f == null) continue;
            if (!_furnitures.Contains(f))
            {
                _furnitures.Add(f);
                anyAdded = true;
            }
            _grid.RegisterFurniture(f, f.transform.position, f.SizeInCells);
        }
        if (anyAdded) InvalidateOwnerBuildingCaches();
    }

#if UNITY_EDITOR
    [ContextMenu("Register Existing Furniture")]
    private void RegisterExistingFurnitureEditor()
    {
        var grid = GetComponent<FurnitureGrid>();
        if (grid == null || !grid.IsInitialized)
        {
            Debug.LogError($"<color=red>[FurnitureManager]</color> Initialize the FurnitureGrid first (right-click FurnitureGrid → Initialize Furniture Grid).");
            return;
        }

        UnityEditor.Undo.RecordObject(this, "Register Existing Furniture");

        _furnitures = new List<Furniture>(GetComponentsInChildren<Furniture>());

        // Restore grid runtime state to register positions
        grid.RestoreFromSerializedData();
        foreach (var f in _furnitures)
        {
            grid.RegisterFurniture(f, f.transform.position, f.SizeInCells);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"<color=green>[FurnitureManager]</color> Registered {_furnitures.Count} existing furniture in editor for {gameObject.name}.");
    }
#endif
}
