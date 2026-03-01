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

    private GridCell[,] _grid;
    private int _gridWidth;
    private int _gridDepth;
    private Vector3 _gridOrigin;
    private BoxCollider _buildingBounds;

    public float CellSize => _cellSize;

    public void Initialize(BoxCollider buildingBounds)
    {
        _buildingBounds = buildingBounds;
        
        if (_buildingBounds == null)
        {
            Debug.LogError($"<color=red>[FurnitureGrid]</color> Aucun BoxCollider fourni pour générer la grille sur {gameObject.name}");
            return;
        }

        Vector3 size = _buildingBounds.size;
        
        // Calcul du nombre de cellules basé sur la taille du collider du building
        _gridWidth = Mathf.CeilToInt(size.x / _cellSize);
        _gridDepth = Mathf.CeilToInt(size.z / _cellSize);

        _grid = new GridCell[_gridWidth, _gridDepth];

        // Calculer l'origine (coin inférieur gauche en X/Z) local ou global
        // Le BoxCollider a un 'center' qui est l'offset local par rapport au transform
        Vector3 globalCenter = transform.TransformPoint(_buildingBounds.center);
        _gridOrigin = globalCenter - new Vector3(size.x / 2f, 0f, size.z / 2f);

        GenerateGrid();
        Debug.Log($"<color=cyan>[FurnitureGrid]</color> Grille initialisée pour {gameObject.name} : {_gridWidth}x{_gridDepth} cellules.");
    }

    private void GenerateGrid()
    {
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridDepth; z++)
            {
                // Position centrale de la cellule (en X/Z). Sur Y, on se place à la base du BoxCollider (le sol).
                float bottomY = transform.TransformPoint(_buildingBounds.center - new Vector3(0, _buildingBounds.size.y / 2f, 0)).y;
                Vector3 cellPos = _gridOrigin + new Vector3(x * _cellSize + _cellSize / 2f, 0, z * _cellSize + _cellSize / 2f);
                cellPos.y = bottomY;
                
                _grid[x, z] = new GridCell
                {
                    WorldPosition = cellPos,
                    Occupant = null,
                    IsWall = false // TODO: Raycast ou check logique pour définir si c'est près d'un mur
                };
            }
        }
    }

    public bool CanPlaceFurniture(Vector3 targetPosition, Vector2Int sizeInCells)
    {
        if (!WorldToGrid(targetPosition, out int startX, out int startZ))
            return false;

        // On vérifie de -size/2 à +size/2 (approx) selon l'ancrage du meuble
        for (int x = startX; x < startX + sizeInCells.x; x++)
        {
            for (int z = startZ; z < startZ + sizeInCells.y; z++)
            {
                if (x < 0 || x >= _gridWidth || z < 0 || z >= _gridDepth)
                    return false; // Hors de la grille (hors du batiment)

                if (_grid[x, z].IsOccupied)
                    return false; // Déjà occupé

                // Pour s'assurer qu'aucun bord du meuble ne traverse le mur,
                // on vérifie les 4 coins de cette cellule spécifique par rapport au BoxCollider
                Vector3 center = _grid[x, z].WorldPosition;
                center.y += 0.5f; // Remonter un peu pour éviter les float issues au sol

                float halfCell = _cellSize / 2.01f; // Léger in-set pour la tolérance des murs
                Vector3 p1 = center + new Vector3(halfCell, 0, halfCell);
                Vector3 p2 = center + new Vector3(-halfCell, 0, halfCell);
                Vector3 p3 = center + new Vector3(halfCell, 0, -halfCell);
                Vector3 p4 = center + new Vector3(-halfCell, 0, -halfCell);

                Bounds roomBounds = _buildingBounds.bounds;
                if (!roomBounds.Contains(p1) || !roomBounds.Contains(p2) || !roomBounds.Contains(p3) || !roomBounds.Contains(p4))
                {
                    return false; // La cellule déborde physiquement du BoxCollider !
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

    public bool GetClosestFreePosition(Vector3 searchOrigin, Vector2Int sizeInCells, out Vector3 bestPosition)
    {
        bestPosition = Vector3.zero;
        float bestSqrDist = float.MaxValue;
        bool found = false;

        for (int x = 0; x <= _gridWidth - sizeInCells.x; x++)
        {
            for (int z = 0; z <= _gridDepth - sizeInCells.y; z++)
            {
                // On simule une tentative de placement
                Vector3 candidatePos = _grid[x, z].WorldPosition;
                // Ajustement : si sizeInCells > 1, le point d'ancrage exact dépend de l'objet, on approxime ici
                
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
    /// Cherche aléatoirement une position valide sur la grille pour un meuble donné.
    /// Retourne la position Vector3 monde, ou null s'il n'y a plus de place.
    /// </summary>
    public Vector3? GetRandomFreePosition(Vector2Int sizeInCells)
    {
        List<Vector3> validPositions = new List<Vector3>();

        // Parcourt toute la grille pour compiler les emplacements valides
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
        if (_grid == null) return;

        Gizmos.color = new Color(0, 1, 0, 0.2f);
        for (int x = 0; x < _gridWidth; x++)
        {
            for (int z = 0; z < _gridDepth; z++)
            {
                Vector3 pos = _grid[x, z].WorldPosition;
                if (_grid[x, z].IsOccupied)
                    Gizmos.color = new Color(1, 0, 0, 0.4f);
                else
                    Gizmos.color = new Color(0, 1, 0, 0.2f);

                Gizmos.DrawCube(pos, new Vector3(_cellSize * 0.9f, 0.1f, _cellSize * 0.9f));
            }
        }
    }
}
