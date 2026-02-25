namespace MWI.AI
{
    /// <summary>
    /// Wrappe MoveOutOfBattleZoneBehaviour dans le BT.
    /// </summary>
    public class BTAction_MoveOutOfBattleZone : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            BattleManager bm = bb.Get<BattleManager>(Blackboard.KEY_BATTLE_MANAGER);
            if (bm == null) return null;
            return new MoveOutOfBattleZoneBehaviour(bm);
        }
    }
}
