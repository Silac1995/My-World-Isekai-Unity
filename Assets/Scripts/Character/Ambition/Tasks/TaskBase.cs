using System;

namespace MWI.Ambition
{
    /// <summary>
    /// Polymorphic, [SerializeReference]-friendly base for atomic verb primitives.
    /// Subclasses (Task_KillCharacter, Task_TalkToCharacter, etc.) implement the
    /// behavior. Each task is bound once via Bind(ctx) when the host AmbitionQuest
    /// is issued; thereafter Tick is called by the BT (NPC side) or its world-state
    /// listeners fire (player side). See spec section "Task → behavior translation".
    /// </summary>
    [Serializable]
    public abstract class TaskBase
    {
        /// <summary>Resolve TaskParameterBindings against the context. Called once on issue / on load.</summary>
        public abstract void Bind(AmbitionContext ctx);

        /// <summary>BT-side per-tick. Idempotent: should re-evaluate world state and only act if needed.</summary>
        public abstract TaskStatus Tick(Character npc, AmbitionContext ctx);

        /// <summary>Called when the task is being unwound (ambition cleared, replaced, or AnyOf sibling won).</summary>
        public abstract void Cancel();

        /// <summary>Hook for switching driver patterns at controller flip. Default: no-op.</summary>
        public virtual void OnControllerSwitching(Character npc, ControllerKind goingTo) { }

        /// <summary>Persist mid-pursuit state. Default: no state. Override to return a JSON / fixed-shape string.</summary>
        public virtual string SerializeState() => string.Empty;

        /// <summary>Restore mid-pursuit state from the SerializeState payload.</summary>
        public virtual void DeserializeState(string s) { }

        /// <summary>Subscribe to world-state events that fire even when the BT isn't ticking the task (player path).</summary>
        public virtual void RegisterCompletionListeners(Character npc, AmbitionContext ctx) { }

        /// <summary>Drop the listeners subscribed in RegisterCompletionListeners.</summary>
        public virtual void UnregisterCompletionListeners(Character npc) { }

        /// <summary>True iff the bound parameters resolved successfully and the task is ready to Tick.</summary>
        public virtual bool IsReady => true;
    }
}
