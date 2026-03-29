using UnityEngine;
using UnityEngine.AI;
using MWI.AI;

namespace MWI.CharacterControllers.Commands
{
    /// <summary>
    /// Command that auto-navigates the player character toward a selected InteractableObject.
    /// Once the player's rigidbody enters the target's InteractionZone, the interaction is triggered
    /// and the command completes.
    /// Follows the same IPlayerCommand pattern as PlayerMoveCommand and PlayerCombatCommand.
    /// </summary>
    public class PlayerInteractCommand : IPlayerCommand
    {
        private readonly InteractableObject _target;
        private readonly PlayerInteractionDetector _detector;
        private bool _hasInteracted;
        private bool _navigationStarted;

        public PlayerInteractCommand(InteractableObject target, PlayerInteractionDetector detector)
        {
            _target = target;
            _detector = detector;
            _hasInteracted = false;
            _navigationStarted = false;
        }

        public bool Tick(PlayerController controller, CharacterMovement movement)
        {
            // Target was destroyed or became null
            if (_target == null)
            {
                Debug.Log("<color=yellow>[PlayerInteractCmd]</color> Target is null. Aborting command.");
                return true;
            }

            // Already interacted — just finish
            if (_hasInteracted)
                return true;

            // Check if the player is now inside the target's InteractionZone
            if (_detector.IsTargetInRange(_target))
            {
                Debug.Log($"<color=green>[PlayerInteractCmd]</color> Arrived at {_target.name}. Triggering interaction.");
                movement.Stop();
                _detector.TriggerInteract(_target);
                _hasInteracted = true;
                return true;
            }

            // Navigate toward the target's position
            Vector3 targetPos = _target.Rigidbody != null
                ? _target.Rigidbody.position
                : _target.transform.position;

            // Find the closest walkable NavMesh point. Use a large radius to handle
            // targets inside big carved areas (e.g. door in a 30m building).
            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 30f, NavMesh.AllAreas))
            {
                targetPos = hit.position;
            }

            if (Vector3.Distance(movement.Destination, targetPos) > 0.5f)
            {
                movement.SetDestination(targetPos, controller.Character.MovementSpeed);
                _navigationStarted = true;
            }

            // Proximity fallback: the player explicitly pressed E on this target.
            // If the agent has walked as close as the NavMesh allows but the target
            // is in a carved area (no trigger overlap possible), interact by distance.
            if (_navigationStarted && NavMeshUtility.HasAgentReachedDestination(movement))
            {
                float dist = Vector3.Distance(controller.transform.position, _target.transform.position);
                Debug.Log($"<color=green>[PlayerInteractCmd]</color> Proximity fallback: {dist:F1}m from {_target.name}. Triggering interaction.");
                movement.Stop();
                _detector.TriggerInteract(_target);
                _hasInteracted = true;
                return true;
            }

            return false;
        }

        public void OnCancelled(PlayerController controller)
        {
            // WASD override or other cancellation — nothing special to clean up
        }
    }
}
