using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Reads its value from the AmbitionContext at Resolve time. The canonical pattern
    /// for inter-step parameter passing — Step 1 writes context["Lover"], Step 2 reads
    /// it via ContextBinding&lt;Character&gt;("Lover").
    /// </summary>
    [Serializable]
    public class ContextBinding<T> : TaskParameterBinding<T>
    {
        public string Key;

        public override T Resolve(AmbitionContext ctx) => ctx.Get<T>(Key);
        public override bool CanResolve(AmbitionContext ctx)
        {
            return ctx != null && !string.IsNullOrEmpty(Key) && ctx.TryGet<T>(Key, out _);
        }
    }
}
