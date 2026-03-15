using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Condition : un ennemi a Ã©tÃ© dÃ©tectÃ© et le NPC dÃ©cide d'attaquer.
    /// Reproduit la logique d'agression spontanÃ©e et anti-ennemi de NPCController.
    /// </summary>
    public class BTCond_DetectedEnemy : BTNode
    {
        private float _checkInterval = 2f;
        private float _lastCheckTime = -999f;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsFree()) return BTNodeStatus.Failure;
            if (self.CharacterRelation == null) return BTNodeStatus.Failure;

            if (UnityEngine.Time.time - _lastCheckTime < _checkInterval) return BTNodeStatus.Failure;
            _lastCheckTime = UnityEngine.Time.time;

            var awareness = self.CharacterAwareness;
            if (awareness == null) return BTNodeStatus.Failure;

            var visibleCharacters = awareness.GetVisibleInteractables<CharacterInteractable>();

            foreach (var interactable in visibleCharacters)
            {
                Character target = interactable.Character;
                if (target == null || !target.IsAlive() || target == self) continue;

                // --- Agression spontanée (trait-based) ---
                if (self.CharacterTraits != null)
                {
                    float aggressivity = self.CharacterTraits.GetAggressivity();
                    float aggroChance = aggressivity * aggressivity * 0.3f;

                    if (aggroChance > 0f && Random.value < aggroChance)
                    {
                        Debug.Log($"<color=red>[BT Aggro]</color> {self.CharacterName} attaque {target.CharacterName} spontanément !");
                        if (self.CharacterSpeech != null)
                            self.CharacterSpeech.Say("You're in my way!");

                        bb.Set(Blackboard.KEY_DETECTED_CHARACTER, target);
                        bb.Set(Blackboard.KEY_COMBAT_TARGET, target);
                        return BTNodeStatus.Success;
                    }
                }

                // --- Agression envers les ennemis connus ---
                var rel = self.CharacterRelation.GetRelationshipWith(target);
                if (rel != null && rel.RelationValue <= -10)
                {
                    float aggroChance = 0.2f;
                    if (self.CharacterTraits != null)
                        aggroChance += self.CharacterTraits.GetAggressivity() * 0.5f;

                    if (Random.value < aggroChance)
                    {
                        Debug.Log($"<color=red>[BT Aggro]</color> {self.CharacterName} repère son ennemi {target.CharacterName} et attaque !");
                        bb.Set(Blackboard.KEY_DETECTED_CHARACTER, target);
                        bb.Set(Blackboard.KEY_COMBAT_TARGET, target);
                        return BTNodeStatus.Success;
                    }
                }
            }

            return BTNodeStatus.Failure;
        }
    }
}
