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
            // No bed available — just sleep standing at home
            Debug.Log($"<color=orange>[Sleep]</color> {character.CharacterName} found no available bed. Sleeping at home anyway.");
            movement.ResetPath();
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
            bool ok;
            if (_bed is BedFurniture bedFurniture)
            {
                int slotIdx = bedFurniture.GetSlotIndexFor(character);
                if (slotIdx < 0) slotIdx = bedFurniture.FindFreeSlotIndex();
                ok = slotIdx >= 0 && bedFurniture.UseSlot(slotIdx, character);
            }
            else
            {
                ok = _bed.Use(character);  // legacy plain-Furniture fallback
            }

            if (ok)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"<color=cyan>[Sleep]</color> {character.CharacterName} is now sleeping in {_bed.FurnitureName}.");
#endif
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Sleep]</color> {character.CharacterName} failed to occupy {_bed.FurnitureName}. Sleeping standing.");
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

        // Save player profile and world state to disk after sleeping.
        // Skip if a TimeSkipController-driven skip is in flight — that path owns
        // its own post-skip save and would otherwise race with this one against
        // a half-state hibernated map.
        if (character.IsServer && character.IsPlayer())
        {
            bool skipInFlight = MWI.Time.TimeSkipController.Instance != null
                                && MWI.Time.TimeSkipController.Instance.IsSkipping;
            if (!skipInFlight && SaveManager.Instance != null)
                SaveManager.Instance.RequestSave(character);
        }
    }

    public void Terminate()
    {
        _isFinished = true;
    }
}
