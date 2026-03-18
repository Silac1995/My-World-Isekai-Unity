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

                    // Gated curve to avoid permanent bloodbaths at low/medium aggressivity.
                    // Must be highly aggressive (>= 0.7) to spontaneously attack a random stranger.
                    if (aggressivity >= 0.7f)
                    {
                        // Ex: à 1.0 -> 0.3^2 = 0.09 * 0.2 = ~1.8% de chance par cible toutes les 2s
                        // Ex: à 0.8 -> 0.1^2 = 0.01 * 0.2 = ~0.2% de chance par cible toutes les 2s
                        float aggroChance = Mathf.Pow(aggressivity - 0.7f, 2f) * 0.2f;

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
                }

                // --- Agression envers les ennemis connus ---
                var rel = self.CharacterRelation.GetRelationshipWith(target);
                if (rel != null && rel.RelationValue <= -10)
                {
                    // Much gentler curve: Base 2% chance (+1% per 0.1 aggro).
                    // Prevents 25% coinflips every 2 seconds causing instant battles across town.
                    float aggroChance = 0.02f;
                    if (self.CharacterTraits != null)
                    {
                        aggroChance += self.CharacterTraits.GetAggressivity() * 0.10f;
                    }

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
