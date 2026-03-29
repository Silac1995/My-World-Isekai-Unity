using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactable attaché à un meuble.
/// Classe de base pour tous les meubles avec lesquels un personnage peut interagir.
/// Gère l'occupation du meuble et le positionnement au point d'interaction.
/// </summary>
public class FurnitureInteractable : InteractableObject
{
    [SerializeField] private Furniture _furniture;
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;

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
                    if (!interactor.CharacterActions.ExecuteAction(action))
                    {
                        _toastChannel?.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                            message: $"Cannot pick up {_furniture.FurnitureName}",
                            type: MWI.UI.Notifications.ToastType.Warning,
                            duration: 3f
                        ));
                    }
                }
            });
        }

        return options.Count > 0 ? options : null;
    }
}
