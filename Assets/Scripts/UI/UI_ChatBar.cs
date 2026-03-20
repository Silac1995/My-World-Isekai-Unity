using UnityEngine;
using TMPro;

public class UI_ChatBar : MonoBehaviour
{
    [SerializeField] private TMP_InputField _inputField;
    private Character _character;
    private int _lastSubmitFrame = -1;
    private CanvasGroup _canvasGroup;

    private void Start()
    {
        if (_inputField == null)
        {
            _inputField = GetComponentInChildren<TMP_InputField>();
        }

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        UpdateVisibility(false);

        if (_inputField != null)
        {
            _inputField.onSubmit.AddListener(OnSubmitChat);
        }
        else
        {
            Debug.LogError("<color=orange>[UI_ChatBar]</color> Missing TMP_InputField component.");
        }
    }

    private void OnDestroy()
    {
        if (_inputField != null)
        {
            _inputField.onSubmit.RemoveListener(OnSubmitChat);
        }
    }

    public void Initialize(Character character)
    {
        _character = character;
    }

    private void Update()
    {
        // Try to focus chat if player presses Enter while not focused
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (_inputField != null && !_inputField.isFocused && Time.frameCount != _lastSubmitFrame)
            {
                _inputField.ActivateInputField();
                _inputField.Select();
            }
        }

        if (_inputField != null)
        {
            UpdateVisibility(_inputField.isFocused);
        }
    }

    private void UpdateVisibility(bool visible)
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            // Removed: _canvasGroup.interactable = visible;
            // The CanvasGroup must remain interactable=true globally, 
            // otherwise EventSystem refuses to `.Select()` the InputField via script!
            _canvasGroup.blocksRaycasts = visible;
        }
    }

    private void OnSubmitChat(string text)
    {
        _lastSubmitFrame = Time.frameCount;

        if (string.IsNullOrWhiteSpace(text)) 
        {
            if (_inputField != null)
            {
                _inputField.text = string.Empty;
                _inputField.DeactivateInputField();
            }
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
            return;
        }

        if (_character != null)
        {
            if (_character.CharacterSpeech != null)
            {
                _character.CharacterSpeech.Say(text);
                
                if (_inputField != null)
                {
                    _inputField.text = string.Empty;
                    _inputField.DeactivateInputField();
                }
                if (UnityEngine.EventSystems.EventSystem.current != null)
                {
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                }
            }
            else
            {
                Debug.LogWarning("<color=orange>[UI_ChatBar]</color> Local player does not have a CharacterSpeech component.");
            }
        }
        else
        {
            Debug.LogWarning("<color=orange>[UI_ChatBar]</color> Character not set. Cannot send chat message.");
        }
    }
}
