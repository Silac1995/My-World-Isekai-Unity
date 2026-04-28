using UnityEngine;

/// <summary>
/// NPC behaviour that navigates to the character's home, finds a bed,
/// and sleeps until the schedule changes.
/// </summary>
public class SleepBehaviour : IAIBehaviour
{
    private enum SleepPhase
    {
        GoingHome,
        FindingBed,
        GoingToBed,
        Sleeping
    }

    private NPCController _npcController;
    private ResidentialBuilding _homeBuilding;
    private Furniture _bed;
    private SleepPhase _phase;
    private bool _isFinished;
    private bool _destinationSet;

    public bool IsFinished => _isFinished;

    public SleepBehaviour(NPCController npc)
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
            Debug.Log($"<color=orange>[Sleep]</color> {character.CharacterName} has no home. Falling back to idle.");
            _isFinished = true;
            return;
        }

        _phase = SleepPhase.GoingHome;
    }

    public void Act(Character character)
    {
        if (_isFinished) return;

        var movement = character.CharacterMovement;
        if (movement == null) return;

        switch (_phase)
        {
            case SleepPhase.GoingHome:
                HandleGoingHome(character, movement);
                break;

            case SleepPhase.FindingBed:
                HandleFindingBed(character, movement);
                break;

            case SleepPhase.GoingToBed:
                HandleGoingToBed(character, movement);
                break;

            case SleepPhase.Sleeping:
                // Stay idle — schedule system will switch activity when sleep time ends
                break;
        }
    }

    private void HandleGoingHome(Character character, CharacterMovement movement)
    {
        if (!_destinationSet)
        {
            Vector3 homePoint = _homeBuilding.GetRandomPointInBuildingZone(character.transform.position.y);
            movement.SetDestination(homePoint);
            _destinationSet = true;
            return;
        }

        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            _phase = SleepPhase.FindingBed;
            _destinationSet = false;
        }
    }

    private void HandleFindingBed(Character character, CharacterMovement movement)
    {
        _bed = character.CharacterLocations.GetAssignedBed();

        if (_bed != null && _bed.Reserve(character))
        {
            _phase = SleepPhase.GoingToBed;
            _destinationSet = false;
        }
        else
        {
            // No bed available — sleep standing at home. Route through CharacterAction
            // so player parity holds (rule #22).
            Debug.Log($"<color=orange>[Sleep]</color> {character.CharacterName} found no available bed. Sleeping at home anyway.");
            movement.ResetPath();

            var action = new CharacterAction_Sleep(character);
            if (character.CharacterActions != null)
            {
                character.CharacterActions.ExecuteAction(action);
            }

            _phase = SleepPhase.Sleeping;
        }
    }

    private void HandleGoingToBed(Character character, CharacterMovement movement)
    {
        if (!_destinationSet)
        {
            movement.SetDestination(_bed.GetInteractionPosition());
            _destinationSet = true;
            return;
        }

        if (!movement.PathPending && (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f))
        {
            // Per rule #22: enqueue the CharacterAction wrapper instead of mutating
            // bed/character state directly. The action calls bed.UseSlot internally,
            // which chains to Character.EnterSleep.
            CharacterAction action = null;
            if (_bed is BedFurniture bedFurniture)
            {
                int slotIdx = bedFurniture.GetSlotIndexFor(character);
                if (slotIdx < 0) slotIdx = bedFurniture.FindFreeSlotIndex();
                if (slotIdx >= 0)
                {
                    action = new CharacterAction_SleepOnFurniture(character, bedFurniture, slotIdx);
                }
            }
            else
            {
                // Legacy plain-Furniture fallback: direct Use as before. No CharacterAction
                // wrapper exists for non-BedFurniture beds; existing scenes that haven't
                // been migrated keep working.
                if (_bed.Use(character))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} legacy-occupied {_bed.FurnitureName}.");
#endif
                }
            }

            if (action != null)
            {
                if (character.CharacterActions != null && character.CharacterActions.ExecuteAction(action))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} enqueued sleep on {_bed.FurnitureName}.");
#endif
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[Sleep]</color> {character.CharacterName} failed to enqueue CharacterAction_SleepOnFurniture on {_bed.FurnitureName}. Falling back to standing.");
                }
            }

            movement.ResetPath();
            _phase = SleepPhase.Sleeping;
        }
    }

    public void Exit(Character character)
    {
        if (_bed != null)
        {
            if (_bed is BedFurniture bedFurniture)
            {
                int slotIdx = bedFurniture.GetSlotIndexFor(character);
                if (slotIdx >= 0) bedFurniture.ReleaseSlot(slotIdx);
            }
            else if (_bed.Occupant == character || _bed.ReservedBy == character)
            {
                _bed.Release();
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} woke up and left {_bed.FurnitureName}.");
#endif
        }

        character.CharacterMovement?.ResetPath();

        // No SaveManager call here. Per the new design, only the time-skip end
        // path triggers a save (avoids churn on accidental wakes). Player sleep
        // saves are owned by TimeSkipController.RunSkip's post-skip loop.
        // NPCs don't trigger save anyway (player-only gate).
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
