namespace MWI.Ambition
{
    /// <summary>
    /// Per-tick result of TaskBase.Tick. Failed is reserved for a future
    /// iteration; v1 tasks never return it (zombie-tolerance per the patient
    /// lifecycle model).
    /// </summary>
    public enum TaskStatus
    {
        Running,
        Completed,
        Failed
    }
}
