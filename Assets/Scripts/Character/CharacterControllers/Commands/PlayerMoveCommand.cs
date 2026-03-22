using UnityEngine;
using MWI.AI;

namespace MWI.CharacterControllers.Commands
{
    public class PlayerMoveCommand : IPlayerCommand
    {
        private Vector3 _destination;
        private float _stopDistance;

        public PlayerMoveCommand(Vector3 destination, float stopDistance = 0.5f)
        {
            _destination = destination;
            _stopDistance = stopDistance;
        }

        public bool Tick(PlayerController controller, CharacterMovement movement)
        {
            if (Vector3.Distance(controller.transform.position, _destination) <= _stopDistance + movement.StoppingDistance)
            {
                return true; 
            }

            if (Vector3.Distance(movement.Destination, _destination) > 0.5f)
            {
                movement.SetDestination(_destination, controller.Character.MovementSpeed);
            }
            return false;
        }

        public void OnCancelled(PlayerController controller)
        {
        }
    }
}
