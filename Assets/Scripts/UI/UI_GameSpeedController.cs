using UnityEngine;
using UnityEngine.UI;
using MWI.Time;

namespace MWI.UI
{
    /// <summary>
    /// UI component that binds interface buttons to the multiplayer GameSpeedController.
    /// It automatically updates button visuals (active/inactive states) when the server changes the game speed.
    /// </summary>
    public class UI_GameSpeedController : MonoBehaviour
    {
        [Header("Speed Buttons")]
        [SerializeField, Tooltip("Sets speed to 0x")] private Button _pauseButton;
        [SerializeField, Tooltip("Sets speed to 1x")] private Button _normalSpeedButton;
        [SerializeField, Tooltip("Sets speed to 2x")] private Button _fastSpeedButton;
        [SerializeField, Tooltip("Sets speed to 4x")] private Button _superFastSpeedButton;
        [SerializeField, Tooltip("Sets speed to 8x")] private Button _gigaSpeedButton;

        [Header("Visual Feedback Settings")]
        [SerializeField, Tooltip("Tint applied to the currently active speed button.")]
        private Color _activeColor = Color.green;
        [SerializeField, Tooltip("Tint applied to inactive buttons.")]
        private Color _inactiveColor = Color.white;

        private void Start()
        {
            // Bind buttons to request a speed change on the network
            if (_pauseButton) _pauseButton.onClick.AddListener(() => RequestSpeed(0f));
            if (_normalSpeedButton) _normalSpeedButton.onClick.AddListener(() => RequestSpeed(1f));
            if (_fastSpeedButton) _fastSpeedButton.onClick.AddListener(() => RequestSpeed(2f));
            if (_superFastSpeedButton) _superFastSpeedButton.onClick.AddListener(() => RequestSpeed(4f));
            if (_gigaSpeedButton) _gigaSpeedButton.onClick.AddListener(() => RequestSpeed(8f));
        }

        private void OnEnable()
        {
            // Subscribe to network speed changes
            if (GameSpeedController.Instance != null)
            {
                GameSpeedController.Instance.OnSpeedChanged += UpdateVisuals;
                UpdateVisuals(UnityEngine.Time.timeScale); // Initial setup
            }
            else
            {
                // In case the UI is enabled before the instance is ready, update with the local timeScale
                UpdateVisuals(UnityEngine.Time.timeScale);
            }
        }

        private void OnDisable()
        {
            // Always unsubscribe to prevent memory leaks
            if (GameSpeedController.Instance != null)
            {
                GameSpeedController.Instance.OnSpeedChanged -= UpdateVisuals;
            }
        }

        /// <summary>
        /// Attempts to notify the server of a desired speed change.
        /// </summary>
        private void RequestSpeed(float targetSpeed)
        {
            if (GameSpeedController.Instance != null)
            {
                GameSpeedController.Instance.RequestSpeedChange(targetSpeed);
            }
            else
            {
                Debug.LogWarning("[UI_GameSpeedController] GameSpeedController instance not found! The NetworkObject might not be spawned yet.");
            }
        }

        /// <summary>
        /// Called locally whenever the time scale changes (via server sync).
        /// Refreshes the color state of all buttons.
        /// </summary>
        private void UpdateVisuals(float currentSpeed)
        {
            // Check approximate float equality
            UpdateButtonVisual(_pauseButton, Mathf.Approximately(currentSpeed, 0f));
            UpdateButtonVisual(_normalSpeedButton, Mathf.Approximately(currentSpeed, 1f));
            UpdateButtonVisual(_fastSpeedButton, Mathf.Approximately(currentSpeed, 2f));
            UpdateButtonVisual(_superFastSpeedButton, Mathf.Approximately(currentSpeed, 4f));
            UpdateButtonVisual(_gigaSpeedButton, currentSpeed >= 7.9f);
        }

        /// <summary>
        /// Modifies the button's visual to indicate if it's currently active.
        /// By default, tints the Target Graphic (Image), but could easily be modified to use Sprite Swap or Canvas Groups.
        /// </summary>
        private void UpdateButtonVisual(Button btn, bool isActive)
        {
            if (btn == null) return;
            
            if (btn.targetGraphic != null)
            {
                btn.targetGraphic.color = isActive ? _activeColor : _inactiveColor;
            }
        }
    }
}
