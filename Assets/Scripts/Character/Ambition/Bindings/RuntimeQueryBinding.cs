using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Picks its value at runtime from world state (e.g. "any eligible lover in this
    /// map"). Concrete subclasses override Resolve to run the query; on success, the
    /// resolved value is written back into ctx[WriteKey] so downstream tasks can read
    /// it via a ContextBinding. Subclasses live next to the Task they support.
    /// </summary>
    [Serializable]
    public abstract class RuntimeQueryBinding<T> : TaskParameterBinding<T>
    {
        public string WriteKey;

        protected abstract T Query(Character npc, AmbitionContext ctx);

        public override T Resolve(AmbitionContext ctx)
        {
            // Resolve without an npc reference: re-read from the cache key written
            // during the first npc-bound resolution. Tasks that need fresh queries
            // should call ResolveWithCharacter.
            if (!string.IsNullOrEmpty(WriteKey) && ctx != null && ctx.TryGet<T>(WriteKey, out var cached))
                return cached;
            return default;
        }

        public T ResolveWithCharacter(Character npc, AmbitionContext ctx)
        {
            // If we already wrote a value, re-use it (idempotent across save/load,
            // controller switches, and BT re-ticks).
            if (!string.IsNullOrEmpty(WriteKey) && ctx != null && ctx.TryGet<T>(WriteKey, out var cached) && cached != null)
                return cached;

            var picked = Query(npc, ctx);
            if (picked != null && !string.IsNullOrEmpty(WriteKey) && ctx != null)
                ctx.Set(WriteKey, picked);
            return picked;
        }

        public override bool CanResolve(AmbitionContext ctx)
        {
            if (string.IsNullOrEmpty(WriteKey) || ctx == null) return false;
            return ctx.TryGet<T>(WriteKey, out var cached) && cached != null;
        }
    }
}
