using UnityEngine;

/// <summary>
/// Classe de base pour tous les meubles placés dans les buildings.
/// Un meuble a une position dans le monde, un occupant potentiel,
/// et un point d'interaction où le personnage doit se placer pour l'utiliser.
/// </summary>
public class Furniture : MonoBehaviour
{
    [Header("Furniture Info")]
    [SerializeField] private string _furnitureName;
    [SerializeField] private Transform _interactionPoint;

    private Character _occupant;

    public string FurnitureName => _furnitureName;
    public Transform InteractionPoint => _interactionPoint;
    public Character Occupant => _occupant;
    public bool IsOccupied => _occupant != null;

    /// <summary>
    /// Un personnage utilise ce meuble.
    /// </summary>
    public bool Use(Character character)
    {
        if (character == null) return false;
        if (IsOccupied)
        {
            Debug.Log($"<color=orange>[Furniture]</color> {_furnitureName} est déjà utilisé par {_occupant.CharacterName}.");
            return false;
        }

        _occupant = character;
        Debug.Log($"<color=cyan>[Furniture]</color> {character.CharacterName} utilise {_furnitureName}.");
        return true;
    }

    /// <summary>
    /// Libère le meuble.
    /// </summary>
    public void Release()
    {
        if (_occupant != null)
        {
            Debug.Log($"<color=cyan>[Furniture]</color> {_occupant.CharacterName} quitte {_furnitureName}.");
        }
        _occupant = null;
    }

    /// <summary>
    /// Position où le personnage doit se placer pour utiliser le meuble.
    /// Si pas d'InteractionPoint défini, utilise la position du meuble.
    /// </summary>
    public Vector3 GetInteractionPosition()
    {
        return _interactionPoint != null ? _interactionPoint.position : transform.position;
    }
}
