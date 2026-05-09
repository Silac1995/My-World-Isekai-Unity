namespace MWI.Ambition
{
    /// <summary>
    /// How a QuestSO sequences its child Tasks.
    /// Sequential — one at a time, in list order; Quest completes when the last task does.
    /// Parallel    — all tick simultaneously; Quest completes when all are Completed.
    /// AnyOf       — all tick simultaneously; Quest completes when any one is Completed
    ///               (remaining tasks are Cancel-led).
    /// </summary>
    public enum TaskOrderingMode
    {
        Sequential,
        Parallel,
        AnyOf
    }
}
