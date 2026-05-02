using MWI.Quests;

namespace MWI.Ambition
{
    /// <summary>
    /// Bridge contract: an IQuest that can also be ticked by the Behaviour Tree
    /// (NPC side) and bound to an AmbitionContext. Lives in CharacterQuestLog like
    /// any other IQuest, but the BT may also drive its tasks directly. See spec
    /// section "Behaviour Tree Integration" — TickActiveTasks delegates to the
    /// concrete QuestSO ordering policy.
    /// </summary>
    public interface IAmbitionStepQuest : IQuest
    {
        void BindContext(AmbitionContext ctx);
        TaskStatus TickActiveTasks(Character npc);
        void Cancel();
        void OnControllerSwitching(Character npc, ControllerKind goingTo);
    }
}
