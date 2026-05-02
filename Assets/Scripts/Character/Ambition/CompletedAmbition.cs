using System;

namespace MWI.Ambition
{
    /// <summary>
    /// History record pushed onto CharacterAmbition.History when an ambition transitions
    /// out of Active. Carries enough info for downstream consumers (dialogue triggers,
    /// reputation effects, dev inspector) to render or react to the achievement.
    /// </summary>
    [Serializable]
    public sealed class CompletedAmbition
    {
        public AmbitionSO SO;
        public AmbitionContext FinalContext;
        public int CompletedDay;
        public CompletionReason Reason;

        public CompletedAmbition() { }

        public CompletedAmbition(AmbitionSO so, AmbitionContext finalContext, int completedDay, CompletionReason reason)
        {
            SO = so;
            FinalContext = finalContext;
            CompletedDay = completedDay;
            Reason = reason;
        }
    }
}
