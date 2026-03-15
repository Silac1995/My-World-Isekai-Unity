# Behaviour Tree Native Node Pattern
This example demonstrates how to create a native `BTNode` for the NPC Behaviour Tree. Native nodes directly manipulate the character's state and movement without relying on the legacy `IAIBehaviour` stack.

```csharp
using UnityEngine;
using MWI.AI;

namespace MWI.AI.Actions
{
    /// <summary>
    /// Example of a native BTNode that makes the NPC perform a specific action,
    /// such as moving to a location and waiting.
    /// </summary>
    public class BTAction_ExampleRoutine : BTNode
    {
        // State variables must be reset in OnExit to avoid leaking into the next execution
        private bool _isMoving = false;
        private float _waitTime = 2f;
        private float _waitStartTime = 0f;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            
            // Check availability
            if (self == null || !self.IsFree() || !self.IsAlive())
                return ResetAndFail(self);

            // 1. Movement Phase
            if (!_isMoving && _waitStartTime == 0f)
            {
                var movement = self.CharacterMovement;
                if (movement != null)
                {
                    // Example: Move to the center of the world
                    movement.SetDestination(Vector3.zero);
                    _isMoving = true;
                }
                return BTNodeStatus.Running; // Tell the BT we are still working
            }

            if (_isMoving)
            {
                var movement = self.CharacterMovement;
                
                // Wait until the path is complete
                if (movement != null && !movement.HasPath && !movement.PathPending)
                {
                    _isMoving = false;
                    _waitStartTime = UnityEngine.Time.time; // Start waiting phase
                }
                
                // Still moving or waiting for path calculation
                return BTNodeStatus.Running; 
            }

            // 2. Action Phase (Waiting)
            // Use UnityEngine.Time.time instead of Time.deltaTime because BT ticks are staggered
            if (UnityEngine.Time.time - _waitStartTime < _waitTime)
            {
                // The action is still in progress
                return BTNodeStatus.Running;
            }

            // 3. Completion
            // The routine is finished. Return Success so the parent node knows it completed.
            return BTNodeStatus.Success;
        }

        protected override void OnExit(Blackboard bb)
        {
            // Cleanup: Always stop movement and reset state variables when exiting the node,
            // whether it succeeded, failed, or was interrupted by a higher priority node.
            if (_isMoving && bb.Self != null && bb.Self.CharacterMovement != null)
            {
                bb.Self.CharacterMovement.Stop();
            }
            
            _isMoving = false;
            _waitStartTime = 0f;
        }

        private BTNodeStatus ResetAndFail(Character self)
        {
            if (_isMoving && self != null && self.CharacterMovement != null)
            {
                self.CharacterMovement.Stop();
            }
            _isMoving = false;
            _waitStartTime = 0f;
            return BTNodeStatus.Failure;
        }
    }
}
```
