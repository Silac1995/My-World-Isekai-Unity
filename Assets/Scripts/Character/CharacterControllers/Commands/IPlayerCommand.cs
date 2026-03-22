using UnityEngine;

namespace MWI.CharacterControllers.Commands
{
    public interface IPlayerCommand
    {
        /// <summary>
        /// Evaluates the command every frame.
        /// </summary>
        /// <param name="controller">The player controller executing the command.</param>
        /// <param name="movement">The character movement component.</param>
        /// <returns>True if the command is complete or safely aborted.</returns>
        bool Tick(PlayerController controller, CharacterMovement movement);

        /// <summary>
        /// Called when the command is interrupted (e.g., by manual WASD input).
        /// </summary>
        void OnCancelled(PlayerController controller);
    }
}
