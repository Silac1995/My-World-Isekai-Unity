using UnityEngine;

namespace MWI.UI.Management
{
    /// <summary>
    /// Implemented by a MonoBehaviour on each tab's prefab. The panel re-parents
    /// <see cref="Root"/> under its body container after <see cref="IManagementTab.CreateView"/>
    /// returns and invokes the lifecycle hooks below.
    ///
    /// Lifecycle order:
    ///   CreateView → OnTabActivated (initial) → (OnTabDeactivated / OnTabActivated cycles per pill click) → Dispose
    ///
    /// <see cref="Dispose"/> MUST unsubscribe from any events the view subscribed to in its
    /// Bind/Awake (rule #16) and Destroy(<see cref="Root"/>) so the GameObject doesn't leak.
    /// </summary>
    public interface IManagementTabView
    {
        /// <summary>The instantiated GameObject the panel re-parents under its body.</summary>
        GameObject Root { get; }

        /// <summary>
        /// User clicked the header pill, OR the panel just opened on this tab, OR the panel
        /// re-opened on the same building (warm-path re-Show). MAY be called more than once
        /// on the same active tab — implementations MUST be idempotent (no per-call allocations,
        /// no duplicate event subscriptions).
        /// </summary>
        void OnTabActivated();

        /// <summary>User switched away — pause subscriptions if expensive (most views: no-op).</summary>
        void OnTabDeactivated();

        /// <summary>Panel closing or rebinding — unsubscribe events, free refs, Destroy(Root).</summary>
        void Dispose();
    }
}
