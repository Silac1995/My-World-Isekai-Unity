

namespace MWI.AI
{
    /// <summary>
    /// Action Node for NodeCanvas Behavior Tree that tells a Mentor to give a live class.
    /// </summary>
    public class BTAction_GiveLesson : BTActionNode
    {
        protected override IAIBehaviour CreateBehaviour(Blackboard bb)
        {
            return new GiveLessonBehaviour();
        }
    }
}
