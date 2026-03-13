using System;
using UnityEngine;

namespace MWI.UI.Notifications
{
    /// <summary>
    /// A decoupled event channel for UI notifications.
    /// Any system can raise a notification without knowing about the UI.
    /// UI components subscribe to these channels to show/hide badges.
    /// </summary>
    [CreateAssetMenu(fileName = "NotificationChannel", menuName = "MWI/UI/Notification Channel")]
    public class NotificationChannel : ScriptableObject
    {
        public event Action OnNotificationRaised;
        public event Action OnNotificationCleared;

        /// <summary>
        /// Call this when something new happens (e.g., new item, new quest).
        /// </summary>
        public void Raise() => OnNotificationRaised?.Invoke();

        /// <summary>
        /// Call this when the player acknowledges the new content (e.g., opens window).
        /// </summary>
        public void Clear() => OnNotificationCleared?.Invoke();
    }
}
