using UnityEngine;
using System.Collections.Generic;

public class FurnitureGrid : MonoBehaviour
{
    [System.Serializable]
    public class GridCell
    {
        public Vector3 WorldPosition;
        public Furniture Occupant;
        public bool IsWall;

        public bool IsOccupied => Occupant != null;
    }

    [Header("Grid Settings")]
    [SerializeField] private float _cellSize = 1f;

    [Tooltip("Floor planes/meshes that define the walkable area. Cells not above any of these are marked as walls.")]
    [SerializeField] private List<Renderer> _floorRenderers = new List<Renderer>();

    [Header("Serialized Grid Data (Editor-Initialized)")]
    [SerializeField] private int _gridWidth;
    [SerializeField] private int _gridDepth;
    [SerializeField] private Vector3 _gridOrigin;
    [SerializeField] private List<GridCell> _cells = new List<GridCell>();

    private GridCell[,] _grid;
    private BoxCollider _buildingBounds;

    public float CellSize => _cellSize;
    public bool IsInitialized => _gridWidth > 0 && _gridDepth > 0 && _cells.Count == _gridWidth * _gridDepth;

    public void Initialize(BoxCollider buildingBounds)
    {
        _buildingBounds = buildingBounds;

        if (_buildingBounds == null)
        {
            Debug.LogError($"<color=red>[FurnitureGrid]</color> No BoxCollider provided for grid generation on {gameObject.name}");
            return;
        }

        Vector3 size = _buildingBounds.size;

        _gridWidth = Mathf.CeilToInt(size.x / _cellSize);
        _gridDepth = Mathf.CeilToInt(size.z / _cellSize);

        Vector3 globalCenter = transform.TransformPoint(_buildingBounds.center);
        _gridOrigin = globalCenter - new Vector3(size.x / 2f, 0f, size.z / 2f);

        GenerateGrid();
        Debug.Log($"<color=cyan>[FurnitureGrid]</color> Grid initialized for {gameObject.name}: {_gridWidth}x{_gridDepth} cells.");
    }

    /// <summary>
    /// Rebuilds the runtime 2D array from serialized flat _cells list.
    /// Call this in Awake when the grid was pre-baked in the editor.
    /// </summary>
    public void RestoreFromSerializedData()
    {
        if (!IsInitialized)
        {
            Debug.LogWarning($"<color=orange>[FurnitureGrid]</color> Cannot restore grid for {gameObject.name}: no serialized data.");
            return;
        }

        _buildingBounds = GetComponent<BoxCollider>();

        // Recalculate grid origin from current transform position (handles interiors at y=5000)
        // The serialized _gridOrigin was baked at edit-time position which may differ at runtime
        if (_buildingBounds != null)
        {
            Vector3 globalCenter = transform.TransformPoint(_buildingBounds.center);
            Vector3 size = _buildingBounds.size;
            _gridOrigin = globalCenter - new Vector3(size.x / 2f, 0f, size.z / 2f);
        }

        _grid = new GridCell[_gridWidth, _gridDepth];
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridDepth; z++)
            {
                GridCell serializedCell = _cells[x * _gridDepth + z];

                // Recalculate world position from grid coordinates instead of using baked position
                float bottomY = _buildingBounds != null
                    ? transform.TransformPoint(_buildingBounds.center - new Vector3(0, _buildingBounds.size.y / 2f, 0)).y
                    : transform.position.y;
                Vector3 cellPos = _gridOrigin + new Vector3(x * _cellSize + _cellSize / 2f, 0, z * _cellSize + _cellSize / 2f);
                cellPos.y = bottomY;

                _grid[x, z] = new GridCell
                {
                    WorldPosition = cellPos,
                    Occupant = null,
                    IsWall = serializedCell.IsWall // Preserve the baked wall data
                };
            }
        }

        Debug.Log($"<color=cyan>[FurnitureGrid]</color> Grid restored for {gameObject.name}: {_gridWidth}x{_gridDepth} cells. Origin: {_gridOrigin}");
    }

#if UNITY_EDITOR
    [ContextMenu("Initialize Furniture Grid")]
    public void InitializeFurnitureGridEditor()
    {
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol == null)
        {
            Debug.LogError($"<color=red>[FurnitureGrid]</color> No BoxCollider found on {gameObject.name}. Add one first.");
            return;
        }

        UnityEditor.Undo.RecordObject(this, "Initialize Furniture Grid");

        Initialize(boxCol);

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"<color=green>[FurnitureGrid]</color> Furniture grid initialized in editor for {gameObject.name}: {_gridWidth}x{_gridDepth} cells.");
    }
#endif

    private void GenerateGrid()
    {
        _grid = new GridCell[_gridWidth, _gridDepth];
        _cells.Clear();

        // Collect floor colliders for raycasting (temporarily enable if needed)
        var floorColliders = new List<Collider>();
        var wasDisabled = new List<Collider>();
        foreach (var r in _floorRenderers)
        {
            if (r == null) continue;
            Collider col = r.GetComponent<Collider>();
            if (col == null) continue;
            if (!col.enabled)
            {
                col.enabled = true;
                wasDisabled.Add(col);
            }
            floorColliders.Add(col);
        }
        bool useFloorCheck = floorColliders.Count > 0;

        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridDepth; z++)
            {
                float bottomY = transform.TransformPoint(_buildingBounds.center - new Vector3(0, _buildingBounds.size.y / 2f, 0)).y;
                Vector3 cellPos = _gridOrigin + new Vector3(x * _cellSize + _cellSize / 2f, 0, z * _cellSize + _cellSize / 2f);
                cellPos.y = bottomY;

                bool isWall = false;
                if (useFloorCheck)
                {
                    // Raycast downward from above the cell to check if it hits any floor renderer
                    Vector3 rayOrigin = cellPos + Vector3.up * 5f;
                    isWall = !RayHitsAnyFloor(rayOrigin, Vector3.down, 10f, floorColliders);
                }

                var cell = new GridCell
                {
                    WorldPosition = cellPos,
                    Occupant = null,
                    IsWall = isWall
                };

                _grid[x, z] = cell;
                _cells.Add(cell);
            }
        }

        // Restore colliders that were disabled
        foreach (var col in wasDisabled)
        {
            col.enabled = false;
        }
    }

    private bool RayHitsAnyFloor(Vector3 origin, Vector3 direction, float maxDistance, List<Collider> floorColliders)
    {
        Ray ray = new Ray(origin, direction);
        foreach (var col in floorColliders)
        {
            if (col.Raycast(ray, out _, maxDistance))
                return true;
        }
        return false;
    }

    public bool CanPlaceFurniture(Vector3 targetPosition, Vector2Int sizeInCells)
    {
        if (!WorldToGrid(targetPosition, out int startX, out int startZ))
            return false;

        for (int x = startX; x < startX + sizeInCells.x; x++)
        {
            for (int z = startZ; z < startZ + sizeInCells.y; z++)
            {
                if (x < 0 || x >= _gridWidth || z < 0 || z >= _gridDepth)
                    return false;

                if (_grid[x, z].IsOccupied || _grid[x, z].IsWall)
                    return false;

                // Check that the cell corners fall within the room's BoxCollider on the X/Z plane.
                // We use the bounds center Y so flat colliders (height=0) don't reject valid cells.
                Bounds roomBounds = _buildingBounds.bounds;
                Vector3 center = _grid[x, z].WorldPosition;
                center.y = roomBounds.center.y; // Match the bounds Y to avoid flat-bounds rejection

                float halfCell = _cellSize / 2.01f;
                Vector3 p1 = center + new Vector3(halfCell, 0, halfCell);
                Vector3 p2 = center + new Vector3(-halfCell, 0, halfCell);
                Vector3 p3 = center + new Vector3(halfCell, 0, -halfCell);
                Vector3 p4 = center + new Vector3(-halfCell, 0, -halfCell);

                if (!roomBounds.Contains(p1) || !roomBounds.Contains(p2) || !roomBounds.Contains(p3) || !roomBounds.Contains(p4))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void RegisterFurniture(Furniture furniture, Vector3 targetPosition, Vector2Int sizeInCells)
    {
        if (!WorldToGrid(targetPosition, out int startX, out int startZ))
            return;

        for (int x = startX; x < startX + sizeInCells.x; x++)
        {
            for (int z = startZ; z < startZ + sizeInCells.y; z++)
            {
                if (x >= 0 && x < _gridWidth && z >= 0 && z < _gridDepth)
                {
                    _grid[x, z].Occupant = furniture;
                }
            }
        }
    }

    public void UnregisterFurniture(Furniture furniture)
    {
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridDepth; z++)
            {
                if (_grid[x, z].Occupant == furniture)
                {
                    _grid[x, z].Occupant = null;
                }
            }
        }
    }

    public bool WorldToGrid(Vector3 worldPos, out int gridX, out int gridZ)
    {
        gridX = 0;
        gridZ = 0;

        Vector3 localPos = worldPos - _gridOrigin;

        int x = Mathf.FloorToInt(localPos.x / _cellSize);
        int z = Mathf.FloorToInt(localPos.z / _cellSize);

        if (x >= 0 && x < _gridWidth && z >= 0 && z < _gridDepth)
        {
            gridX = x;
            gridZ = z;
            return true;
        }

        return false;
    }

    public Vector3 GetCellCenter(int x, int z)
    {
        if (x >= 0 && x < _gridWidth && z >= 0 && z < _gridDepth)
        {
            return _grid[x, z].WorldPosition;
        }
        return Vector3.zero;
    }

    /// <summary>
    /// Snaps a world position to the nearest grid cell center.
    /// Returns false if the position is outside the grid.
    /// </summary>
    public bool SnapToGrid(Vector3 worldPos, out Vector3 snappedPos)
    {
        snappedPos = worldPos;
        if (WorldToGrid(worldPos, out int gx, out int gz))
        {
            snappedPos = _grid[gx, gz].WorldPosition;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Given a cursor world position and furniture size, returns the grid-snapped anchor position
    /// (the first/bottom-left cell) such that the furniture is centered on the cursor cell.
    /// Also returns the visual center position for ghost rendering.
    /// Returns false if the cursor is outside the grid.
    /// </summary>
    public bool GetPlacementPositions(Vector3 cursorWorldPos, Vector2Int sizeInCells,
        out Vector3 anchorPos, out Vector3 visualCenterPos)
    {
        anchorPos = cursorWorldPos;
        visualCenterPos = cursorWorldPos;

        if (!WorldToGrid(cursorWorldPos, out int cursorX, out int cursorZ))
            return false;

        // Center the furniture footprint on the cursor cell
        int anchorX = cursorX - (sizeInCells.x - 1) / 2;
        int anchorZ = cursorZ - (sizeInCells.y - 1) / 2;

        // Clamp to grid bounds so the footprint doesn't extend outside
        anchorX = Mathf.Clamp(anchorX, 0, _gridWidth - sizeInCells.x);
        anchorZ = Mathf.Clamp(anchorZ, 0, _gridDepth - sizeInCells.y);

        // Anchor = first cell position (what CanPlaceFurniture/RegisterFurniture expect)
        anchorPos = _grid[anchorX, anchorZ].WorldPosition;

        // Visual center = midpoint of the full footprint
        int endX = Mathf.Min(anchorX + sizeInCells.x - 1, _gridWidth - 1);
        int endZ = Mathf.Min(anchorZ + sizeInCells.y - 1, _gridDepth - 1);
        Vector3 endPos = _grid[endX, endZ].WorldPosition;
        visualCenterPos = (anchorPos + endPos) / 2f;

        return true;
    }

    public bool GetClosestFreePosition(Vector3 searchOrigin, Vector2Int sizeInCells, out Vector3 bestPosition)
    {
        bestPosition = Vector3.zero;
        float bestSqrDist = float.MaxValue;
        bool found = false;

        for (int x = 0; x <= _gridWidth - sizeInCells.x; x++)
        {
            for (int z = 0; z <= _gridDepth - sizeInCells.y; z++)
            {
                // Simulate a placement attempt
                Vector3 candidatePos = _grid[x, z].WorldPosition;
                // Adjustment: if sizeInCells > 1, the exact anchor point depends on the object, we approximate here
                
                if (CanPlaceFurniture(candidatePos, sizeInCells))
                {
                    float sqrDist = (candidatePos - searchOrigin).sqrMagnitude;
                    if (sqrDist < bestSqrDist)
                    {
                        bestSqrDist = sqrDist;
                        bestPosition = candidatePos;
                        found = true;
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Randomly searches for a valid position on the grid for a given piece of furniture.
    /// Returns the world Vector3 position, or null if there is no more room.
    /// </summary>
    public Vector3? GetRandomFreePosition(Vector2Int sizeInCells)
    {
        List<Vector3> validPositions = new List<Vector3>();

        // Iterate over the entire grid to compile valid locations
        for (int x = 0; x <= _gridWidth - sizeInCells.x; x++)
        {
            for (int z = 0; z <= _gridDepth - sizeInCells.y; z++)
            {
                Vector3 candidatePos = _grid[x, z].WorldPosition;
                if (CanPlaceFurniture(candidatePos, sizeInCells))
                {
                    validPositions.Add(candidatePos);
                }
            }
        }

        if (validPositions.Count > 0)
        {
            return validPositions[Random.Range(0, validPositions.Count)];
        }

        return null;
    }

    private void OnDrawGizmosSelected()
    {
        // Use runtime 2D array if available, otherwise fall back to serialized flat list
        if (_grid != null)
        {
            for (int x = 0; x < _gridWidth; x++)
            {
                for (int z = 0; z < _gridDepth; z++)
                {
                    DrawCellGizmo(_grid[x, z]);
                }
            }
        }
        else if (IsInitialized)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                DrawCellGizmo(_cells[i]);
            }
        }
    }

    private void DrawCellGizmo(GridCell cell)
    {
        if (cell.IsWall)
            Gizmos.color = new Color(0.3f, 0.3f, 0.3f, 0.15f); // Gray = wall / no floor
        else if (cell.IsOccupied)
            Gizmos.color = new Color(1, 0, 0, 0.4f); // Red = occupied
        else
            Gizmos.color = new Color(0, 1, 0, 0.2f); // Green = free

        Gizmos.DrawCube(cell.WorldPosition, new Vector3(_cellSize * 0.9f, 0.1f, _cellSize * 0.9f));
    }
}
