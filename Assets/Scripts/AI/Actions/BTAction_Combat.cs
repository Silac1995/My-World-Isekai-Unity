using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Wrappe CombatBehaviour dans le BT.
    /// Running tant que le combat est actif, Success quand il se termine.
    /// </summary>
    public class BTAction_Combat : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            Character self = bb.Self;
            BattleManager battleManager = bb.Get<BattleManager>(Blackboard.KEY_BATTLE_MANAGER);
            Character target = bb.Get<Character>(Blackboard.KEY_COMBAT_TARGET);

            if (self == null || battleManager == null) return null;

            return new CombatBehaviour(battleManager, target);
        }

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            // On met à jour la cible si elle a changé dans le blackboard
            Character self = bb.Self;
            if (self == null || !self.CharacterCombat.IsInBattle)
                return BTNodeStatus.Success;

            return base.OnExecute(bb);
        }
    }
}
