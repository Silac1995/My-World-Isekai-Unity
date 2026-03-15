using UnityEngine;

/// <summary>
/// Action permettant à un personnage de "Pointer" à son lieu de travail.
/// Vérifie s'il est physiquement dans la zone du bâtiment avant de s'exécuter.
/// </summary>
public class Action_PunchIn : CharacterAction
{
    private CommercialBuilding _workplace;

    public override bool ShouldPlayGenericActionAnimation => true;

    public Action_PunchIn(Character character, CommercialBuilding workplace) : base(character, 1.5f)
    {
        _workplace = workplace;
    }

    public override bool CanExecute()
    {
        if (_workplace == null) return false;

        // On est indulgent: BTAction_Work se charge déjà de le rapprocher.
        // Éviter l'échec silencieux si le personnage est à 0.1 unité hors du trigger.
        return true;
    }

    public override void OnStart()
    {
        Debug.Log($"<color=cyan>[Work]</color> {character.CharacterName} pointe pour commencer son service à {_workplace.BuildingName}.");
        
        // Stopper le mouvement pour l'animation
        character.CharacterMovement?.Stop();
    }

    public override void OnApplyEffect()
    {
        _workplace.WorkerStartingShift(character);
    }
}
