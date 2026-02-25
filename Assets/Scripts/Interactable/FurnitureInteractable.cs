using UnityEngine;

/// <summary>
/// Interactable attaché à un meuble.
/// Classe de base pour tous les meubles avec lesquels un personnage peut interagir.
/// Gère l'occupation du meuble et le positionnement au point d'interaction.
/// </summary>
public class FurnitureInteractable : InteractableObject
{
    [SerializeField] private Furniture _furniture;

    public Furniture Furniture => _furniture;
    public bool IsOccupied => _furniture != null && _furniture.IsOccupied;

    protected virtual void Awake()
    {
        if (_furniture == null)
            _furniture = GetComponent<Furniture>();
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _furniture == null) return;

        if (IsOccupied)
        {
            Debug.Log($"<color=orange>[Furniture]</color> {_furniture.FurnitureName} est déjà occupé par {_furniture.Occupant.CharacterName}.");
            return;
        }

        // Occuper le meuble
        if (_furniture.Use(interactor))
        {
            OnFurnitureUsed(interactor);
        }
    }

    /// <summary>
    /// Appelé quand un personnage commence à utiliser le meuble.
    /// Override dans les sous-classes pour la logique spécifique.
    /// </summary>
    protected virtual void OnFurnitureUsed(Character user)
    {
        Debug.Log($"<color=cyan>[Furniture]</color> {user.CharacterName} utilise {_furniture.FurnitureName}.");
    }

    /// <summary>
    /// Libère le meuble et le personnage.
    /// </summary>
    public virtual void Release()
    {
        if (_furniture != null)
        {
            _furniture.Release();
        }
    }
}
