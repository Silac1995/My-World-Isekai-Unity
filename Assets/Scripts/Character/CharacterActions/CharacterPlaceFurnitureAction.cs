using UnityEngine;

public class CharacterPlaceFurnitureAction : CharacterAction
{
    private Room _targetRoom;
    private Furniture _furniturePrefab;
    private Vector3 _targetPosition;
    private bool _hasTargetPosition;
    
    // Constructeur 1 : On connaît la position exacte voulue
    public CharacterPlaceFurnitureAction(Character character, Room room, Furniture furniturePrefab, Vector3 targetPosition, float duration = 1.0f) 
        : base(character, duration)
    {
        _targetRoom = room;
        _furniturePrefab = furniturePrefab;
        _targetPosition = targetPosition;
        _hasTargetPosition = true;
    }

    // Constructeur 2 : Le système cherchera l'emplacement libre le plus proche du personnage
    public CharacterPlaceFurnitureAction(Character character, Room room, Furniture furniturePrefab, float duration = 1.0f) 
        : base(character, duration)
    {
        _targetRoom = room;
        _furniturePrefab = furniturePrefab;
        _hasTargetPosition = false;
    }

    public override bool CanExecute()
    {
        if (_targetRoom == null || _furniturePrefab == null) 
            return false;

        FurnitureGrid grid = _targetRoom.Grid;
        if (grid == null) 
            return false;

        // Si on n'a pas de position, on cherche la plus proche maintenant
        if (!_hasTargetPosition)
        {
            if (grid.GetClosestFreePosition(character.transform.position, _furniturePrefab.SizeInCells, out Vector3 bestPos))
            {
                _targetPosition = bestPos;
                _hasTargetPosition = true; // On a verrouillé une position
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Action]</color> Impossible de trouver une place libre pour {_furniturePrefab.FurnitureName} dans {_targetRoom.RoomName}.");
                return false;
            }
        }

        // Vérifie si la position trouvée/donnée est (toujours) valide (limite la vérification à la Room elle-même et à la grille)
        return _targetRoom.IsPointInsideRoom(_targetPosition) && grid.CanPlaceFurniture(_targetPosition, _furniturePrefab.SizeInCells);
    }

    public override void OnStart()
    {
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null)
        {
            animator.SetTrigger("IsDoingAction"); 
        }

        // On peut faire se tourner le personnage vers la cible
        character.CharacterVisual?.FaceTarget(_targetPosition);

        Debug.Log($"<color=cyan>[Action]</color> {character.CharacterName} installe {_furniturePrefab.FurnitureName}.");
    }

    public override void OnApplyEffect()
    {
        if (_targetRoom != null && _furniturePrefab != null)
        {
            bool success = _targetRoom.AddFurniture(_furniturePrefab, _targetPosition);
            if (success)
            {
                Debug.Log($"<color=cyan>[Action]</color> {_furniturePrefab.FurnitureName} placé avec succès !");
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Action]</color> Échec du placement de {_furniturePrefab.FurnitureName} à la dernière minute (emplacement pris).");
            }
        }
    }
}
