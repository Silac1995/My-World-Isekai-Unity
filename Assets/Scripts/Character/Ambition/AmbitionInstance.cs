using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Runtime state of an active ambition on a Character. Owned by CharacterAmbition.
    /// CurrentStepQuest mirrors a CharacterQuestLog entry for the active step.
    /// </summary>
    [Serializable]
    public sealed class AmbitionInstance
    {
        public AmbitionSO SO;
        public int CurrentStepIndex;
        public IAmbitionStepQuest CurrentStepQuest;
        public AmbitionContext Context;
        public int AssignedDay;

        public AmbitionInstance() { Context = new AmbitionContext(); }

        public bool IsLastStep => SO != null && CurrentStepIndex >= SO.Quests.Count - 1;
        public int TotalSteps => SO != null ? SO.Quests.Count : 0;
        public float Progress01
        {
            get
            {
                if (SO == null || SO.Quests.Count == 0) return 0f;
                return (float)CurrentStepIndex / SO.Quests.Count;
            }
        }
    }
}
