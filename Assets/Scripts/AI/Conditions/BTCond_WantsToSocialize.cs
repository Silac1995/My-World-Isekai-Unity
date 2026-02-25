using UnityEngine;
using System.Linq;

namespace MWI.AI
{
    /// <summary>
    /// Condition : le NPC veut socialiser avec un personnage détecté.
    /// Reproduit la logique sociale de NPCController (sociabilité, compatibilité).
    /// </summary>
    public class BTCond_WantsToSocialize : BTNode
    {
        private float _checkInterval = 10f;
        private float _lastCheckTime = -999f;
        private float _socialCooldown = 60f; // Cooldown après une interaction réussie
        private float _lastSocialTime = -999f;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsFree()) return BTNodeStatus.Failure;

            // Cooldown après la dernière socialisation
            if (UnityEngine.Time.time - _lastSocialTime < _socialCooldown) return BTNodeStatus.Failure;

            // Déjà en interaction
            if (self.CharacterInteraction.IsInteracting) return BTNodeStatus.Failure;

            if (UnityEngine.Time.time - _lastCheckTime < _checkInterval) return BTNodeStatus.Failure;
            _lastCheckTime = UnityEngine.Time.time;

            // Vérifier la sociabilité via les traits
            if (self.CharacterTraits != null)
            {
                float sociability = self.CharacterTraits.GetSociability();
                if (Random.value > sociability) return BTNodeStatus.Failure;
            }

            var awareness = self.CharacterAwareness;
            if (awareness == null) return BTNodeStatus.Failure;

            var visibleCharacters = awareness.GetVisibleInteractables<CharacterInteractable>();
            var potentialTargets = visibleCharacters
                .Select(i => i.Character)
                .Where(c => c != null && c != self && c.IsAlive() && c.IsFree()
                    && !c.CharacterInteraction.IsInteracting)
                .ToList();

            if (potentialTargets.Count == 0) return BTNodeStatus.Failure;

            // Choisir la meilleure cible (priorité aux connaissances)
            Character target = potentialTargets
                .OrderByDescending(c =>
                {
                    float relScore = self.CharacterRelation?.GetRelationshipWith(c)?.RelationValue ?? 0f;
                    float distScore = -Vector3.Distance(self.transform.position, c.transform.position);
                    return relScore * 2f + distScore;
                })
                .FirstOrDefault();

            if (target == null) return BTNodeStatus.Failure;

            bb.Set(Blackboard.KEY_SOCIAL_TARGET, target);

            // Initier l'interaction
            NPCController npc = self.Controller as NPCController;
            if (npc == null) return BTNodeStatus.Failure;

            _lastSocialTime = UnityEngine.Time.time; // Cooldown démarre maintenant

            npc.PushBehaviour(new MoveToTargetBehaviour(npc, target.gameObject, 7f, () =>
            {
                if (target == null || !target.IsAlive()) return;
                self.CharacterInteraction.StartInteractionWith(target);
            }));

            Debug.Log($"<color=cyan>[BT Social]</color> {self.CharacterName} engage la conversation avec {target.CharacterName}.");
            return BTNodeStatus.Success;
        }
    }
}
