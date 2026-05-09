using System;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Polymorphic step contract iterated by CinematicDirector.
    /// The director never branches on concrete step type — extension is one new subclass.
    /// </summary>
    public interface ICinematicStep
    {
        /// <summary>Called once when the step becomes active.</summary>
        void OnEnter(CinematicContext ctx);

        /// <summary>Called every director tick while IsComplete returns false.</summary>
        void OnTick(CinematicContext ctx, float dt);

        /// <summary>Called once when the step completes or is aborted.</summary>
        void OnExit(CinematicContext ctx);

        /// <summary>Director polls this; advances when true.</summary>
        bool IsComplete(CinematicContext ctx);
    }

    /// <summary>
    /// Default abstract base. Subclasses override only what they need.
    /// Default IsComplete returns true (instant step) — override for stateful steps.
    /// </summary>
    [Serializable]
    public abstract class CinematicStep : ICinematicStep
    {
        [SerializeField] protected string _label;     // editor display label
        public string Label => string.IsNullOrEmpty(_label) ? GetType().Name : _label;

        public virtual void OnEnter(CinematicContext ctx) { }
        public virtual void OnTick (CinematicContext ctx, float dt) { }
        public virtual void OnExit (CinematicContext ctx) { }
        public virtual bool IsComplete(CinematicContext ctx) => true;
    }
}
