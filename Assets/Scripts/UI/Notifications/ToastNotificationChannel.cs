using System;
using UnityEngine;

namespace MWI.UI.Notifications
{
    /// <summary>
    /// A decoupled event channel for Toast notifications.
    /// Can be triggered locally by clients (e.g. inventory pickup) without knowing about the UI.
    /// </summary>
    [CreateAssetMenu(fileName = "ToastNotificationChannel", menuName = "MWI/UI/Toast Notification Channel")]
    public class ToastNotificationChannel : ScriptableObject
    {
        public event Action<ToastNotificationPayload> OnToastRaised;

        /// <summary>
        /// Raise a new toast notification.
        /// Ensure this is only called on the local client (IsOwner) to prevent server/multiplayer noise.
        /// </summary>
        public void Raise(ToastNotificationPayload payload)
        {
            OnToastRaised?.Invoke(payload);
        }
    }
}
