using UnityEngine;
using MWI.AI;

namespace MWI.CharacterControllers.Commands
{
    public class PlayerCombatCommand : IPlayerCommand
    {
        private readonly CombatAILogic _combatAILogic;
        private readonly Character _target;

        public PlayerCombatCommand(Character player, Character target)
        {
            _target = target;
            // The player executes manual intent, so autoDecide = false
            _combatAILogic = new CombatAILogic(player, false);
            _combatAILogic.OnEnter();
        }

        public bool Tick(PlayerController controller, CharacterMovement movement)
        {
            if (_target == null || !_target.IsAlive())
            {
                movement.Stop();
                return true;
            }

            // Tick action charging / autonomous pacing
            _combatAILogic.Tick(_target);

            // Never fully finish autonomously until the target dies
            return false; 
        }

        public void OnCancelled(PlayerController controller)
        {
            controller.CharacterMovement.Stop();
        }
    }
}
