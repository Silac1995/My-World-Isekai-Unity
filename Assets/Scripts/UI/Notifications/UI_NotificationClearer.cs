using UnityEngine;

namespace MWI.UI.Notifications
{
    /// <summary>
    /// Put this on a Window GameObject (like Inventory) to automatically 
    /// clear a notification channel when the window is opened.
    /// </summary>
    public class UI_NotificationClearer : MonoBehaviour
    {
        [SerializeField] private NotificationChannel _channel;

        private void OnEnable()
        {
            if (_channel != null)
            {
                _channel.Clear();
            }
        }
    }
}
