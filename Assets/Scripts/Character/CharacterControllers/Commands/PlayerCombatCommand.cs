using UnityEngine;
using MWI.AI;

namespace MWI.CharacterControllers.Commands
{
    public class PlayerCombatCommand : IPlayerCommand
    {
        private readonly CombatAILogic _combatAILogic;
        private readonly Character _player;
        private readonly Character _target;

        public PlayerCombatCommand(Character player, Character target)
        {
            _player = player;
            _target = target;
            // The player executes manual intent, so autoDecide = false
            _combatAILogic = new CombatAILogic(player, false);
            _combatAILogic.OnEnter();
        }

        public bool Tick(PlayerController controller, CharacterMovement movement)
        {
            if (_target == null || !_target.IsAlive())
            {
                movement.ResetPath();
                movement.Resume();
                return true;
            }

            // Exit when battle has ended (player is no longer in a battle)
            if (_player != null && !_player.CharacterCombat.IsInBattle)
            {
                movement.ResetPath();
                movement.Resume();
                return true;
            }

            // Tick action charging / autonomous pacing
            _combatAILogic.Tick(_target);

            // Never fully finish autonomously until the target dies or battle ends
            return false;
        }

        public void OnCancelled(PlayerController controller)
        {
            controller.CharacterMovement.ResetPath();
            controller.CharacterMovement.Resume();
        }
    }
}
