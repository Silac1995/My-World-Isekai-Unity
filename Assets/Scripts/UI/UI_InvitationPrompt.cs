using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI
{
    public class UI_InvitationPrompt : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Image _iconImage;
        [SerializeField] private Image _progressBarFill;
        [SerializeField] private Button _acceptButton;
        [SerializeField] private Button _refuseButton;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("Settings")]
        [SerializeField] private float _fadeDuration = 0.3f;

        private CharacterInvitation _currentCharacterInvitation;
        
        private void Awake()
        {
            if (_canvasGroup == null) _canvasGroup = GetComponent<CanvasGroup>();
            
            if (_acceptButton != null)
                _acceptButton.onClick.AddListener(OnAcceptClicked);
                
            if (_refuseButton != null)
                _refuseButton.onClick.AddListener(OnRefuseClicked);
                
            // Ensure we start hidden
            HideImmediately();
        }

        private void OnDestroy()
        {
            if (_acceptButton != null)
                _acceptButton.onClick.RemoveListener(OnAcceptClicked);
                
            if (_refuseButton != null)
                _refuseButton.onClick.RemoveListener(OnRefuseClicked);
                
            UnsubscribeFromCurrent();
        }

        public void Initialize(CharacterInvitation invitationManager)
        {
            UnsubscribeFromCurrent();
            
            _currentCharacterInvitation = invitationManager;
            if (_currentCharacterInvitation != null)
            {
                _currentCharacterInvitation.OnPlayerInvitationReceived += HandleInvitationReceived;
                _currentCharacterInvitation.OnPlayerInvitationTimerUpdated += HandleTimerUpdated;
                _currentCharacterInvitation.OnPlayerInvitationResolved += HidePrompt;
            }
        }
        
        private void UnsubscribeFromCurrent()
        {
            if (_currentCharacterInvitation != null)
            {
                _currentCharacterInvitation.OnPlayerInvitationReceived -= HandleInvitationReceived;
                _currentCharacterInvitation.OnPlayerInvitationTimerUpdated -= HandleTimerUpdated;
                _currentCharacterInvitation.OnPlayerInvitationResolved -= HidePrompt;
            }
        }

        private void HandleInvitationReceived(InteractionInvitation invitation, Character source)
        {
            // Populate UI data
            if (_titleText != null)
            {
                _titleText.text = source.CharacterName;
                _titleText.gameObject.SetActive(true);
            }

            if (_messageText != null)
            {
                _messageText.text = invitation.GetInvitationMessage(source, _currentCharacterInvitation.Character);
            }

            if (_progressBarFill != null)
            {
                _progressBarFill.fillAmount = 1f;
            }

            // Show prompt
            ShowPrompt();
        }

        private void HandleTimerUpdated(float normalizedTimeRemaining)
        {
            if (_progressBarFill != null)
            {
                // normalizedTimeRemaining goes from 0 to 1 where 0 is start, 1 is timeout
                _progressBarFill.fillAmount = Mathf.Clamp01(1f - normalizedTimeRemaining);
            }
        }

        private void OnAcceptClicked()
        {
            Debug.Log("<color=cyan>[UI_InvitationPrompt]</color> OnAcceptClicked FIRED.");
            if (_currentCharacterInvitation != null)
            {
                _currentCharacterInvitation.ResolvePlayerInvitation(true);
            }
            HidePrompt();
        }

        private void OnRefuseClicked()
        {
            Debug.Log("<color=cyan>[UI_InvitationPrompt]</color> OnRefuseClicked FIRED.");
            if (_currentCharacterInvitation != null)
            {
                _currentCharacterInvitation.ResolvePlayerInvitation(false);
            }
            HidePrompt();
        }

        private void ShowPrompt()
        {
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(1f));
        }

        private void HidePrompt()
        {
            StopAllCoroutines();
            StartCoroutine(FadeRoutine(0f, onComplete: () => gameObject.SetActive(false)));
        }

        private void HideImmediately()
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        private IEnumerator FadeRoutine(float targetAlpha, Action onComplete = null)
        {
            float startAlpha = _canvasGroup.alpha;
            float timer = 0f;

            // Make interactable if we are fading in
            if (targetAlpha > 0f)
            {
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
            else
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }

            while (timer < _fadeDuration)
            {
                timer += UnityEngine.Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, timer / _fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = targetAlpha;
            onComplete?.Invoke();
        }
    }
}
