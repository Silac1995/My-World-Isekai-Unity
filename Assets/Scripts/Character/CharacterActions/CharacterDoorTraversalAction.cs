using System.Collections;
using UnityEngine;

/// <summary>
/// Abstract base for actions that walk an actor to a <see cref="MapTransitionDoor"/>
/// and trigger it. Subclasses override <see cref="ResolveDoor"/> (which door to use)
/// and <see cref="IsActionRedundant"/> (whether the action would be a no-op).
///
/// The action never reimplements transition or lock logic — it just navigates to the
/// door and calls <c>door.Interact(actor)</c>, then releases its slot in the action
/// queue so the door can queue <see cref="CharacterMapTransitionAction"/> normally.
///
/// Lifecycle:
///   OnStart → resolve door, freeze controller (NPC), launch walk coroutine.
///   Walk coroutine → repath every 2 s, time out at 15 s, on arrival release self
///   and call door.Interact.
///   OnCancel → stop coroutine, unfreeze controller, stop movement.
/// </summary>
public abstract class CharacterDoorTraversalAction : CharacterAction
{
    private const float WalkTimeoutSeconds = 15f;
    private const float RepathIntervalSeconds = 2f;
    // 0.3 s covers the server-side IsLocked.Value write and one NetworkVariable
    // replication tick (~0.05–0.1 s at default tickrate). This action is intended for
    // server-driven NPCs (per rule #18), so the write is synchronous; a player-driven
    // call would have one round-trip (~RTT + tick), still well under 0.3 s on a healthy
    // connection.
    private const float PostInteractWaitSeconds = 0.3f;

    private Coroutine _walkCoroutine;
    private bool _didFreeze;

    protected CharacterDoorTraversalAction(Character actor) : base(actor, duration: 0f) { }

    /// <summary>
    /// Returns the door this action should navigate to and interact with,
    /// or null if no valid door exists (action will cancel with a warning).
    /// </summary>
    protected abstract MapTransitionDoor ResolveDoor();

    /// <summary>
    /// Returns true when the action is already accomplished (e.g. actor is already
    /// on the destination map). The action cancels cleanly with no warning.
    /// </summary>
    protected abstract bool IsActionRedundant();

    public override void OnStart()
    {
        if (character == null || !character.IsAlive())
        {
            FailAndCancel("[DoorTraversal] Actor is null or dead at start.");
            return;
        }

        if (IsActionRedundant())
        {
            // No-op: silently cancel. Caller's intent is already satisfied.
            character.CharacterActions.ClearCurrentAction();
            return;
        }

        MapTransitionDoor door = ResolveDoor();
        if (door == null)
        {
            FailAndCancel($"[DoorTraversal] {character.CharacterName}: no door resolved.");
            return;
        }

        if (!character.IsPlayer() && character.Controller != null)
        {
            character.Controller.Freeze();
            _didFreeze = true;
        }
        character.CharacterMovement?.Resume();

        _walkCoroutine = character.StartCoroutine(WalkRoutine(door));
    }

    public override void OnApplyEffect()
    {
        // Duration is 0; this fires immediately after OnStart. All real work runs
        // inside the walk coroutine launched in OnStart, so nothing to do here.
    }

    public override void OnCancel()
    {
        if (_walkCoroutine != null && character != null)
        {
            character.StopCoroutine(_walkCoroutine);
            _walkCoroutine = null;
        }

        if (character != null)
        {
            character.CharacterMovement?.Stop();
            if (_didFreeze && character.Controller != null)
            {
                character.Controller.Unfreeze();
                _didFreeze = false;
            }
        }
    }

    private void FailAndCancel(string warning)
    {
        Debug.LogWarning($"<color=orange>{warning}</color>");
        character?.CharacterActions?.ClearCurrentAction();
    }

    private IEnumerator WalkRoutine(MapTransitionDoor door)
    {
        // Pre-check: locked-no-key fails fast (no point walking over).
        // The door itself would also reject us, but bailing now avoids the wasted walk.
        DoorLock doorLock = door.GetComponent<DoorLock>();
        bool wasLocked = doorLock != null && doorLock.IsSpawned && doorLock.IsLocked.Value;
        if (wasLocked)
        {
            KeyInstance key = character.CharacterEquipment?.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);
            if (key == null)
            {
                FailAndCancel($"[DoorTraversal] {character.CharacterName}: door '{door.name}' is locked and no key in inventory.");
                yield break;
            }
        }

        character.CharacterMovement.SetDestination(door.transform.position);

        float elapsed = 0f;
        float timeSinceLastRepath = 0f;

        while (elapsed < WalkTimeoutSeconds)
        {
            if (character == null || !character.IsAlive() || door == null)
            {
                FailAndCancel($"[DoorTraversal] Actor or door became invalid mid-walk.");
                yield break;
            }

            if (door.IsCharacterInInteractionZone(character))
            {
                character.CharacterMovement.Stop();

                // Release our slot so door.Interact can queue CharacterMapTransitionAction.
                // We are still inside this coroutine, so null _walkCoroutine first — that way
                // OnCancel (invoked synchronously by ClearCurrentAction) skips its StopCoroutine
                // call and we keep yielding past the upcoming WaitForSeconds. OnCancel still
                // unfreezes and stops movement, which is exactly the state we want before the
                // door takes over.
                _walkCoroutine = null;
                character.CharacterActions.ClearCurrentAction();

                door.Interact(character);

                // If the door queued a transition action, our job is done.
                yield return new WaitForSeconds(PostInteractWaitSeconds);
                if (character != null
                    && character.CharacterActions.CurrentAction is CharacterMapTransitionAction)
                {
                    yield break;
                }

                // Locked-with-key path: the door called RequestUnlockServerRpc and returned.
                // Give the unlock a moment to replicate, then re-Interact once.
                if (wasLocked && doorLock != null && !doorLock.IsLocked.Value)
                {
                    door.Interact(character);
                    yield return new WaitForSeconds(PostInteractWaitSeconds);
                    if (character != null
                        && character.CharacterActions.CurrentAction is CharacterMapTransitionAction)
                    {
                        yield break;
                    }
                }

                // Door rejected us (rattle case, or some other door gate). Already released, just log.
                Debug.LogWarning($"<color=orange>[DoorTraversal] {character?.CharacterName}: door '{door.name}' refused entry.</color>");
                yield break;
            }

            timeSinceLastRepath += Time.deltaTime;
            if (timeSinceLastRepath >= RepathIntervalSeconds)
            {
                character.CharacterMovement.SetDestination(door.transform.position);
                timeSinceLastRepath = 0f;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        FailAndCancel($"[DoorTraversal] {character?.CharacterName}: timed out walking to '{door.name}' after {WalkTimeoutSeconds}s.");
    }
}
