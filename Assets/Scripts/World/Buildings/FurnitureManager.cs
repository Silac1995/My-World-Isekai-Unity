using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(FurnitureGrid))]
public class FurnitureManager : MonoBehaviour
{
    [Header("Manager Info")]
    [SerializeField] protected List<Furniture> _furnitures = new List<Furniture>();
    
    private FurnitureGrid _grid;
    private Room _room; // Referénce à la room parente pour les logs et la parenté Transform

    public IReadOnlyList<Furniture> Furnitures => _furnitures;
    public FurnitureGrid Grid => _grid;

    private void Awake()
    {
        _grid = GetComponent<FurnitureGrid>();
        _room = GetComponent<Room>();
    }

    /// <summary>
    /// Utilisé pour vérifier si une interface UI ou un PNJ peut placer ce meuble à cet endroit exact.
    /// Renvoie true si l'emplacement est valide sur la grille.
    /// </summary>
    public bool IsPlacementValid(Furniture furniturePrefab, Vector3 targetPosition)
    {
        if (_grid == null || furniturePrefab == null) return false;
        return _grid.CanPlaceFurniture(targetPosition, furniturePrefab.SizeInCells);
    }

    /// <summary>
    /// Essaie d'ajouter un meuble à cette room via le manager.
    /// </summary>
    public bool AddFurniture(Furniture furniturePrefab, Vector3 targetPosition)
    {
        if (_grid == null) return false;

        if (IsPlacementValid(furniturePrefab, targetPosition))
        {
            Furniture newFurniture = Instantiate(furniturePrefab, targetPosition, Quaternion.identity, transform);
            
            // Ajustement du pivot : targetPosition est le centre de la PREMIÈRE cellule (bas-gauche).
            // Si le meuble fait 3x2 cellules, le centre visuel global du meuble doit être décalé 
            // pour être au milieu de ces 3x2 cellules, et non centré sur la seule 1ère cellule.
            Renderer[] renderers = newFurniture.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                // L'espace total réservé sur la grille forme un grand rectangle.
                // On calcule le centre EXACT de ce grand rectangle.
                Vector3 regionCenter = targetPosition + new Vector3(
                    (furniturePrefab.SizeInCells.x - 1) * _grid.CellSize / 2f,
                    0,
                    (furniturePrefab.SizeInCells.y - 1) * _grid.CellSize / 2f
                );

                // On calcule la distance entre le centre actuel du meuble 3D et le centre voulu de la grille
                float offsetX = regionCenter.x - bounds.center.x;
                float offsetZ = regionCenter.z - bounds.center.z;
                
                // Pour la hauteur (Y), on s'assure que le point le plus bas du mesh touche le sol
                float offsetY = targetPosition.y - bounds.min.y;

                newFurniture.transform.position += new Vector3(offsetX, offsetY, offsetZ);
            }

            _furnitures.Add(newFurniture);
            _grid.RegisterFurniture(newFurniture, targetPosition, newFurniture.SizeInCells);
            
            string roomName = _room != null ? _room.RoomName : gameObject.name;
            Debug.Log($"<color=green>[FurnitureManager]</color> Instanciation REUSSIE de {furniturePrefab.name} à {newFurniture.transform.position} dans {roomName} !");
            return true;
        }

        string failRoomName = _room != null ? _room.RoomName : gameObject.name;
        Debug.LogWarning($"<color=orange>[FurnitureManager]</color> Emplacement invalide ou déjà occupé pour le meuble {furniturePrefab.FurnitureName} à {targetPosition} dans {failRoomName}.");
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
            Destroy(furnitureToRemove.gameObject);
        }
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
    /// Peuple la liste initiale des meubles s'ils sont déjà enfants du Transform au lancement du jeu.
    /// </summary>
    public void LoadExistingFurniture()
    {
        if (_grid == null) return;
        
        _furnitures = new List<Furniture>(GetComponentsInChildren<Furniture>());
        foreach (var f in _furnitures)
        {
            _grid.RegisterFurniture(f, f.transform.position, f.SizeInCells);
        }
    }
}
