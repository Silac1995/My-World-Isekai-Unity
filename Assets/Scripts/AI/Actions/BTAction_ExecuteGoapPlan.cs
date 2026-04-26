using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Behaviour Tree action node that drives the CharacterGoapController.
    /// It tries to plan a life goal and executes the corresponding actions.
    /// </summary>
    public class BTAction_ExecuteGoapPlan : BTNode
    {
        private CharacterGoapController _goapController;

        protected override void OnEnter(Blackboard bb)
        {
            if (_goapController == null)
            {
                // Prefer a child GameObject (project convention: subsystems on children, see CLAUDE.md Character Facade pattern).
                // `GetComponentInChildren` also matches a controller on the root itself, so this is strictly more tolerant than `GetComponent`.
                _goapController = bb.Self.GetComponentInChildren<CharacterGoapController>();

                if (_goapController == null)
                {
                    // Legacy prefabs (Character_Default_Humanoid, Character_Default_Quadruped, Character_Animal) don't have
                    // a dedicated GOAPController child. We silently add the component on the root — `CharacterSystem.OnEnable`
                    // auto-registers it with the character's capability registry, so `Character.CharacterGoap` resolves after this.
                    //
                    // IMPORTANT: never `Debug.LogError` in this branch. It fires every BT tick (0.1s) for every such NPC,
                    // and the Unity console accumulation on Windows progressively stalls the editor — that's the exact
                    // "host-only progressive freeze" pattern this module is meant to prevent.
                    _goapController = bb.Self.gameObject.AddComponent<CharacterGoapController>();
                }
            }

            // Try to plan as soon as we enter
            _goapController.Replan();
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            if (_goapController == null) return BTNodeStatus.Failure;

            // If we no longer have an action and can't replan -> done (Success to plug the tree)
            if (_goapController.CurrentAction == null)
            {
                if (!_goapController.Replan())
                {
                    return BTNodeStatus.Failure;
                }
            }

            // Execute the current action
            _goapController.ExecutePlan();

            // While we have a plan, we stay "Running". If it ends this tick, we return Failure to hand off
            // to lower priorities on the next frame (since _goapController.CurrentAction will become null)
            return _goapController.CurrentAction != null ? BTNodeStatus.Running : BTNodeStatus.Failure;
        }

        protected override void OnExit(Blackboard bb)
        {
            // We can choose to keep the plan paused or cancel it.
            // Here we cancel it to avoid ghost behaviours when switching BT branches.
            _goapController?.CancelPlan();
        }
    }
}
