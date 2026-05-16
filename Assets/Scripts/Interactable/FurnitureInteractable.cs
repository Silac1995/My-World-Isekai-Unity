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

    /// <summary>
    /// True only when the underlying furniture is <see cref="IOccupiable"/> AND a
    /// character is currently bound. Non-occupiable furniture (sign, management desk,
    /// storage) is never reported as "occupied" — they have no occupancy state to
    /// query post the 2026-05-08 ISP refactor.
    /// </summary>
    public bool IsOccupied => _furniture is IOccupiable occ && occ.IsOccupied;

    protected virtual void Awake()
    {
        if (_furniture == null)
            _furniture = GetComponent<Furniture>();
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _furniture == null) return;

        // Pre-flight guard for occupiable furniture only — non-occupiable types fall
        // straight through to OnInteract.
        if (_furniture is IOccupiable occ && occ.IsOccupied)
        {
            Debug.Log($"<color=orange>[Furniture]</color> {_furniture.FurnitureName} est déjà occupé par {occ.Occupant.CharacterName}.");
            return;
        }

        // Universal dispatch — OccupiableFurniture.OnInteract delegates to Use();
        // bespoke types (DisplayText / Management) override OnInteract directly.
        if (_furniture.OnInteract(interactor))
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
    /// Libère le meuble et le personnage. No-op when the furniture isn't occupiable.
    /// </summary>
    public virtual void Release()
    {
        if (_furniture is IOccupiable occ)
        {
            occ.Release();
        }
    }

    public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        var options = new List<InteractionOption>();

        // Furniture-type-specific options first (e.g. SafeFurniture's "Open Safe").
        // Contributed by the underlying Furniture via the GetExtraInteractionOptions
        // virtual so each furniture type owns its own verb without subclassing
        // FurnitureInteractable. Pattern mirrors OnInteract — the menu is a
        // Furniture concern, not an Interactable concern.
        if (_furniture != null)
        {
            var extras = _furniture.GetExtraInteractionOptions(interactor);
            if (extras != null) options.AddRange(extras);
        }

        // "Pick Up" option — only for furniture that has a FurnitureItemSO assigned
        if (_furniture != null && _furniture.FurnitureItemSO != null)
        {
            bool isDisabled = _furniture is IOccupiable occ && occ.IsOccupied;
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
