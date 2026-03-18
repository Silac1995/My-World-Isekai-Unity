namespace MWI.AI
{
    /// <summary>
    /// Condition : le NPC est actuellement en combat.
    /// Wrappe CombatBehaviour pour gérer le combat actif.
    /// </summary>
    public class BTCond_IsInCombat : BTNode
    {
        private BTAction_Combat _combatAction = new BTAction_Combat();

        protected override BTNodeStatus OnExecute(Blackboard bb)
        {
            Character self = bb.Self;
            if (self == null || !self.CharacterCombat.IsInBattle)
            {
                return BTNodeStatus.Failure;
            }

            // Mettre à jour le blackboard avec les infos de combat
            BattleManager bm = self.CharacterCombat.CurrentBattleManager;
            if (bm != null)
            {
                bb.Set(Blackboard.KEY_BATTLE_MANAGER, bm);

                // Trouver la cible la plus prioritaire / optimale
                Character target = bm.GetBestTargetFor(self);
                if (target != null)
                    bb.Set(Blackboard.KEY_COMBAT_TARGET, target);
            }

            return _combatAction.Execute(bb);
        }

        protected override void OnExit(Blackboard bb)
        {
            _combatAction.Abort(bb);
            bb.Remove(Blackboard.KEY_BATTLE_MANAGER);
            // On ne supprime pas KEY_COMBAT_TARGET ici, car il est partagé et utilisé par la séquence d'agression.
            // S'il est supprimé à chaque échec d'évaluation (chaque tick où IsInBattle est false),
            // cela annule complètement l'approche de la BTAction_AttackTarget.
        }
    }
}
