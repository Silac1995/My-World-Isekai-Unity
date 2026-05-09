using UnityEngine;

/// <summary>
/// NPC behaviour that navigates the character to their home building.
/// Once arrived, switches to wandering within the home zone.
/// </summary>
public class GoHomeBehaviour : IAIBehaviour
{
    private NPCController _npcController;
    private ResidentialBuilding _homeBuilding;
    private bool _isFinished;
    private bool _destinationSet;

    public bool IsFinished => _isFinished;

    public GoHomeBehaviour(NPCController npc)
    {
        _npcController = npc;
    }

    public void Enter(Character character)
    {
        if (character.CharacterLocations == null)
        {
            _isFinished = true;
            return;
        }

        _homeBuilding = character.CharacterLocations.GetHomeBuilding();

        if (_homeBuilding == null)
        {
            Debug.Log($"<color=orange>[GoHome]</color> {character.CharacterName} has no home. Falling back to wander.");
            _isFinished = true;
            return;
        }
    }

    public void Act(Character character)
    {
        if (_isFinished || _homeBuilding == null) return;

        var movement = character.CharacterMovement;
        if (movement == null) return;

        // Set destination once
        if (!_destinationSet)
        {
            Vector3 homePoint = _homeBuilding.GetRandomPointInBuildingZone(character.transform.position.y);
            movement.SetDestination(homePoint);
            _destinationSet = true;
            return;
        }

        // Check arrival
        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            // Arrived home — switch to wandering within the home zone
            Debug.Log($"<color=cyan>[GoHome]</color> {character.CharacterName} arrived home at {_homeBuilding.BuildingName}.");
            _npcController.SetBehaviour(new WanderBehaviour(_npcController, _homeBuilding));
            _isFinished = true;
        }
    }

    public void Exit(Character character)
    {
        character.CharacterMovement?.ResetPath();
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
