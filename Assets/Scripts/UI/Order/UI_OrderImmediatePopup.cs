// Assets/Scripts/UI/Order/UI_OrderImmediatePopup.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using MWI.Orders;

namespace MWI.UI.Orders
{
    /// <summary>
    /// Player-side popup shown when this client's owned Character receives an
    /// OrderImmediate. Mirrors UI_InvitationPrompt structure. Subscribes to
    /// CharacterOrders.OnOrderPromptShown on the owning Character (bound via
    /// PlayerUI.Initialize → BindToLocalPlayer).
    ///
    /// The timer bar uses Image.fillAmount (matching the source prefab structure
    /// inherited from UI_InvitationPrompt) rather than a Slider.
    /// </summary>
    public class UI_OrderImmediatePopup : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Image           _timerBarFill;
        [SerializeField] private Button          _acceptButton;
        [SerializeField] private Button          _refuseButton;
        [SerializeField] private CanvasGroup     _canvasGroup;

        [Header("Settings")]
        [SerializeField] private float _fadeDuration = 0.3f;

        private CharacterOrders _watchedOrders;
        private ulong           _currentOrderId;
        private float           _timerStart;
        private float           _timeoutSeconds;
        private bool            _active;

        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            HideImmediately();

            if (_acceptButton != null) _acceptButton.onClick.AddListener(() => Respond(true));
            if (_refuseButton != null) _refuseButton.onClick.AddListener(() => Respond(false));
        }

        private void OnDestroy()
        {
            if (_watchedOrders != null) _watchedOrders.OnOrderPromptShown -= HandlePrompt;
            if (_acceptButton != null) _acceptButton.onClick.RemoveAllListeners();
            if (_refuseButton != null) _refuseButton.onClick.RemoveAllListeners();
        }

        /// <summary>
        /// Bind to the local player's CharacterOrders. Called by PlayerUI.Initialize
        /// once the owned Character is known. Pass null to unbind on character swap /
        /// clean-up.
        /// </summary>
        public void BindToLocalPlayer(CharacterOrders orders)
        {
            if (_watchedOrders != null) _watchedOrders.OnOrderPromptShown -= HandlePrompt;
            _watchedOrders = orders;
            if (_watchedOrders != null) _watchedOrders.OnOrderPromptShown += HandlePrompt;
        }

        private void HandlePrompt(PendingOrderSyncData data)
        {
            _currentOrderId = data.OrderId;
            _timeoutSeconds = data.TimeoutSeconds;
            _timerStart     = UnityEngine.Time.unscaledTime; // Rule #26 — UI uses unscaled time
            _active         = true;

            // Resolve issuer display name from the live spawn table (client-visible).
            string issuerName = "Someone";
            if (data.IssuerNetId != 0
                && NetworkManager.Singleton != null
                && NetworkManager.Singleton.SpawnManager.SpawnedObjects
                    .TryGetValue(data.IssuerNetId, out var issuerObj))
            {
                var issuerChar = issuerObj.GetComponent<Character>();
                if (issuerChar != null) issuerName = issuerChar.CharacterName;
            }

            if (_messageText != null)
                _messageText.text = $"{issuerName} orders you: {data.OrderTypeName} (Priority {data.Priority})";

            if (_timerBarFill != null)
                _timerBarFill.fillAmount = 1f;

            ShowPrompt();
        }

        private void Update()
        {
            if (!_active) return;

            // Rule #26 — UI timer runs on unscaled time so it is not affected by
            // GameSpeedController pauses or high-speed simulation.
            float elapsed   = UnityEngine.Time.unscaledTime - _timerStart;
            float remaining = Mathf.Max(0f, _timeoutSeconds - elapsed);

            if (_timerBarFill != null && _timeoutSeconds > 0f)
                _timerBarFill.fillAmount = remaining / _timeoutSeconds;

            if (remaining <= 0f)
            {
                // Auto-refuse on UI-side timeout. Server also auto-refuses after TimeoutSeconds,
                // so this is just a fail-safe to hide the popup cleanly.
                Respond(false);
            }
        }

        private void Respond(bool accept)
        {
            if (!_active) return;
            _active = false;

            if (_watchedOrders != null)
                _watchedOrders.ResolvePlayerOrderServerRpc(_currentOrderId, accept);

            HidePrompt();
        }

        // ── Show / Hide ────────────────────────────────────────────────────

        private void ShowPrompt()
        {
            gameObject.SetActive(true);
            StopAllCoroutines();

            if (_canvasGroup != null)
            {
                _canvasGroup.interactable   = true;
                _canvasGroup.blocksRaycasts = true;
                StartCoroutine(FadeRoutine(1f));
            }
        }

        private void HidePrompt()
        {
            StopAllCoroutines();

            if (_canvasGroup != null)
            {
                _canvasGroup.interactable   = false;
                _canvasGroup.blocksRaycasts = false;
                StartCoroutine(FadeRoutine(0f, onComplete: () => gameObject.SetActive(false)));
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void HideImmediately()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha          = 0f;
                _canvasGroup.interactable   = false;
                _canvasGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
        }

        private System.Collections.IEnumerator FadeRoutine(float targetAlpha, System.Action onComplete = null)
        {
            if (_canvasGroup == null) { onComplete?.Invoke(); yield break; }

            float startAlpha = _canvasGroup.alpha;
            float timer      = 0f;

            while (timer < _fadeDuration)
            {
                timer += UnityEngine.Time.unscaledDeltaTime; // Rule #26 — UI fade uses unscaled time
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / _fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = targetAlpha;
            onComplete?.Invoke();
        }
    }
}
