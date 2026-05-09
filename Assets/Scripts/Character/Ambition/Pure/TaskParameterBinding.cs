using System;

namespace MWI.Ambition
{
    /// <summary>
    /// One typed input slot on a Task. Subclasses resolve to a concrete value from
    /// (a) a static authored value, (b) a key in the AmbitionContext, or (c) a
    /// runtime query that picks dynamically from world state.
    /// Marked [Serializable] so [SerializeReference] in the host Task can author
    /// the subclass choice in the inspector.
    /// </summary>
    [Serializable]
    public abstract class TaskParameterBinding<T>
    {
        public abstract T Resolve(AmbitionContext ctx);

        /// <summary>
        /// True iff resolution will succeed (e.g. the context key exists, the runtime
        /// query has a non-null result). Used by Task.IsReady.
        /// </summary>
        public abstract bool CanResolve(AmbitionContext ctx);
    }
}
