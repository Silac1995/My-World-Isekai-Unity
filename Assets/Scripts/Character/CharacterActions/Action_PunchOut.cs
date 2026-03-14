using UnityEngine;

/// <summary>
/// Action permettant à un personnage de "Dépointer" à la fin de son service.
/// Vérifie s'il est physiquement dans la zone du bâtiment avant de s'exécuter.
/// </summary>
public class Action_PunchOut : CharacterAction
{
    private CommercialBuilding _workplace;

    public override bool ShouldPlayGenericActionAnimation => true;

    // Durée configurable, mais 1.5s par défaut pour correspondre au Punch In
    public Action_PunchOut(Character character, CommercialBuilding workplace) : base(character, 1.5f)
    {
        _workplace = workplace;
    }

    public override bool CanExecute()
    {
        if (_workplace == null || _workplace.BuildingZone == null) return false;

        // Le personnage DOIT être à l'intérieur du Collider de la BuildingZone
        return _workplace.BuildingZone.bounds.Contains(character.transform.position);
    }

    public override void OnStart()
    {
        Debug.Log($"<color=magenta>[Work]</color> {character.CharacterName} dépointe à la fin de son service à {_workplace.BuildingName}.");
        
        // Stopper le mouvement pour l'animation
        character.CharacterMovement?.Stop();
    }

    public override void OnApplyEffect()
    {
        // Se retire de la liste des employés actifs sur place
        _workplace.WorkerEndingShift(character);
    }
}
