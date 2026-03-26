using UnityEngine;

namespace MWI.UI.Notifications
{
    /// <summary>
    /// A static helper to raise toast notifications globally on the local client.
    /// This provides a centralized and easy-to-use API for any script (World, Character, UI).
    /// </summary>
    public static class UI_Toast
    {
        private static ToastNotificationChannel _generalChannel;

        /// <summary>
        /// Initializes the helper with the main toast channel.
        /// Usually called by the local PlayerUI.
        /// </summary>
        public static void Initialize(ToastNotificationChannel generalChannel)
        {
            _generalChannel = generalChannel;
        }

        /// <summary>
        /// Shows a standard toast notification.
        /// </summary>
        public static void Show(string message, ToastType type = ToastType.Info, float duration = 2f, string title = "", Sprite icon = null)
        {
            if (_generalChannel == null)
            {
                // In some cases (e.g. Editor tests or early initialization), the channel might not be set.
                // We log a warning instead of crashing.
                Debug.LogWarning($"[UI_Toast] Cannot show toast '{message}' because the channel is not initialized.");
                return;
            }

            _generalChannel.Raise(new ToastNotificationPayload(message, type, duration, title, icon));
        }

        /// <summary>
        /// Shows a toast using a pre-constructed payload.
        /// </summary>
        public static void Show(ToastNotificationPayload payload)
        {
            if (_generalChannel == null)
            {
                Debug.LogWarning($"[UI_Toast] Cannot show toast because the channel is not initialized.");
                return;
            }

            _generalChannel.Raise(payload);
        }
    }
}
