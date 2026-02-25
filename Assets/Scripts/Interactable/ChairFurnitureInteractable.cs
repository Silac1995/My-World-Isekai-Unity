using UnityEngine;

/// <summary>
/// Interactable pour les sièges (chaise, tabouret, banc...).
/// Quand un personnage interagit, il s'assoit sur le meuble.
/// </summary>
public class ChairFurnitureInteractable : FurnitureInteractable
{
    private ChairFurniture _chair;

    private bool _isSeated = false;
    private Character _seatedCharacter;

    public bool IsSeated => _isSeated;

    protected override void Awake()
    {
        base.Awake();
        _chair = Furniture as ChairFurniture;
    }

    protected override void OnFurnitureUsed(Character user)
    {
        base.OnFurnitureUsed(user);

        if (_chair == null) return;

        // Positionner le personnage sur le siège
        user.CharacterMovement?.SetDestination(_chair.SeatPosition);

        _seatedCharacter = user;
        _isSeated = true;

        // Orienter le personnage
        if (_chair.FaceForward)
        {
            user.CharacterVisual?.FaceTarget(_chair.SeatPosition + _chair.SeatForward);
        }

        // Stopper le personnage une fois assis
        // TODO: Déclencher l'animation sit quand elle existera
        user.Controller?.Freeze();

        Debug.Log($"<color=green>[Chair]</color> {user.CharacterName} s'assoit sur {Furniture.FurnitureName}.");
    }

    /// <summary>
    /// Fait lever le personnage du siège.
    /// </summary>
    public override void Release()
    {
        if (_seatedCharacter != null)
        {
            _seatedCharacter.Controller?.Unfreeze();
            Debug.Log($"<color=green>[Chair]</color> {_seatedCharacter.CharacterName} se lève de {Furniture.FurnitureName}.");
        }

        _isSeated = false;
        _seatedCharacter = null;

        base.Release();
    }
}
