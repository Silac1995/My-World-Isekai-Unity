using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Author-time constant. Useful for Task_WaitDays.Days = 7 or numeric tuning;
    /// rarely used for Character/Zone references which usually come from context.
    /// </summary>
    [Serializable]
    public class StaticBinding<T> : TaskParameterBinding<T>
    {
        public T Value;

        public override T Resolve(AmbitionContext ctx) => Value;
        public override bool CanResolve(AmbitionContext ctx) => Value != null || typeof(T).IsValueType;
    }
}
