using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Condition : un ami ou coÃ©quipier est en combat et a besoin d'aide.
    /// Reproduit la logique d'entraide de NPCController.HandleCharacterDetected().
    /// </summary>
    public class BTCond_FriendInDanger : BTNode
    {
        private float _checkInterval = 2f;
        private float _lastCheckTime = -999f;
        private Character _friendInDanger;

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.IsFree()) return BTNodeStatus.Failure;
            if (self.CharacterRelation == null) return BTNodeStatus.Failure;

            // Ne pas checker trop souvent pour la performance
            if (UnityEngine.Time.time - _lastCheckTime < _checkInterval) return BTNodeStatus.Failure;
            _lastCheckTime = UnityEngine.Time.time;

            // Chercher un ami/coÃ©quipier en combat dans les personnages visibles
            var awareness = self.CharacterAwareness;
            if (awareness == null) return BTNodeStatus.Failure;

            var visibleCharacters = awareness.GetVisibleInteractables<CharacterInteractable>();

            foreach (var interactable in visibleCharacters)
            {
                Character target = interactable.Character;
                if (target == null || !target.IsAlive() || !target.CharacterCombat.IsInBattle)
                    continue;

                bool isFriend = self.CharacterRelation.IsFriend(target);
                bool sameParty = self.CurrentParty != null && self.CurrentParty == target.CurrentParty;

                // Loyalty trait
                bool isAcquaintance = self.CharacterRelation.GetRelationshipWith(target)?.RelationValue >= 0;
                bool isLoyalHelp = self.CharacterTraits != null
                    && self.CharacterTraits.GetLoyalty() > 0.5f
                    && isAcquaintance;

                if (isFriend || sameParty || isLoyalHelp)
                {
                    _friendInDanger = target;

                    string helpMsg = sameParty ? "Protect the group!" : "Hang on, my friend! I'm coming!";
                    
                    // Trouver l'adversaire de l'ami (s'il en a un)
                    _friendInDanger = target;
                    Character enemyToAttack = target.CharacterCombat.CurrentBattleManager?.GetBestTargetFor(target);
                    
                    if (enemyToAttack != null)
                    {
                        float viewRadius = self.CharacterAwareness != null ? self.CharacterAwareness.AwarenessRadius : 15f;
                        if (Vector3.Distance(self.transform.position, enemyToAttack.transform.position) <= viewRadius)
                        {
                            Debug.Log($"<color=green>[BT Assist]</color> {self.CharacterName} voit {target.CharacterName} en combat contre {enemyToAttack.CharacterName} et va l'aider !");
                            
                            if (self.CharacterSpeech != null)
                                self.CharacterSpeech.Say(helpMsg);

                            self.CharacterCombat.JoinBattleAsAlly(target);
                            
                            // Définir la cible pour le BTAction_AttackTarget
                            bb.Set(Blackboard.KEY_COMBAT_TARGET, enemyToAttack);
                            return BTNodeStatus.Success;
                        }
                    }
                }
            }

            return BTNodeStatus.Failure;
        }
    }
}
