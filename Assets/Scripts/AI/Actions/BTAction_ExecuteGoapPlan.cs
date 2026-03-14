using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Node d'action du Behaviour Tree qui pilote le CharacterGoapController.
    /// Il tente de planifier un objectif de vie et exécute les actions correspondantes.
    /// </summary>
    public class BTAction_ExecuteGoapPlan : BTNode
    {
        private CharacterGoapController _goapController;

        protected override void OnEnter(Blackboard bb)
        {
            if (_goapController == null)
            {
                _goapController = bb.Self.GetComponent<CharacterGoapController>();
                if (_goapController == null)
                {
                    // Fallback : Ajouter le component s'il manque
                    _goapController = bb.Self.gameObject.AddComponent<CharacterGoapController>();
                }
            }

            // Tenter de planifier dès l'entrée
            _goapController.Replan();
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            if (_goapController == null) return BTNodeStatus.Failure;

            // Si on n'a plus d'action et qu'on ne peut pas replanifier -> fini (Success pour boucher l'arbre)
            if (_goapController.CurrentAction == null)
            {
                if (!_goapController.Replan())
                {
                    return BTNodeStatus.Failure; 
                }
            }

            // Exécuter l'action en cours
            _goapController.ExecutePlan();

            // Tant qu'on a un plan, on reste en "Running". S'il se termine ce tick, on renvoie Failure pour laisser
            // le relai aux priorités inférieures au prochain frame (puisque _goapController.CurrentAction deviendra null)
            return _goapController.CurrentAction != null ? BTNodeStatus.Running : BTNodeStatus.Failure;
        }

        protected override void OnExit(Blackboard bb)
        {
            // On peut choisir de garder le plan en pause ou de l'annuler
            // Ici on annule pour éviter des comportements fantômes si on change de branche BT
            _goapController?.CancelPlan();
        }
    }
}
