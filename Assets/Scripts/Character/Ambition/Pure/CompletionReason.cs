namespace MWI.Ambition
{
    /// <summary>
    /// Why a CompletedAmbition record exists. v1 uses Completed and ClearedByScript only.
    /// Failed is reserved for a future iteration that adds auto-fail predicates.
    /// </summary>
    public enum CompletionReason
    {
        Completed,
        ClearedByScript,
        Failed
    }
}
