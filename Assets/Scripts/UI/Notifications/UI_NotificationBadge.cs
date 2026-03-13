using UnityEngine;

namespace MWI.UI.Notifications
{
    /// <summary>
    /// A reactive UI component that shows/hides a badge based on a NotificationChannel.
    /// Fully decoupled from game logic.
    /// </summary>
    public class UI_NotificationBadge : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private NotificationChannel _channel;
        [SerializeField] private GameObject          _badgeObject;
        
        [Tooltip("If true, the notification will be cleared automatically when this object's parent window opens (requires custom logic or standard toggle).")]
        [SerializeField] private bool _clearOnEnable = false;

        private void OnEnable()
        {
            if (_channel == null) return;

            _channel.OnNotificationRaised += ShowBadge;
            _channel.OnNotificationCleared += HideBadge;

            if (_clearOnEnable)
            {
                _channel.Clear();
            }
        }

        private void OnDisable()
        {
            if (_channel == null) return;

            _channel.OnNotificationRaised -= ShowBadge;
            _channel.OnNotificationCleared -= HideBadge;
        }

        [Header("Auto-Hide")]
        [Tooltip("Optional: The window this badge belongs to. If this window is active, the badge will stay hidden.")]
        [SerializeField] private GameObject _parentWindow;

        private void ShowBadge()
        {
            // If the window is already open, we don't show the badge
            if (_parentWindow != null && _parentWindow.activeInHierarchy)
            {
                _channel.Clear();
                return;
            }

            if (_badgeObject != null)
                _badgeObject.SetActive(true);
        }

        private void HideBadge()
        {
            if (_badgeObject != null)
                _badgeObject.SetActive(false);
        }
    }
}
