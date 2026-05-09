using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_Action_ProgressBar : MonoBehaviour
{
    private CharacterActions _characterActions;

    [Header("UI References")]
    [SerializeField] private Image _fillImage;
    [SerializeField] private TextMeshProUGUI _actionNameText;

    // This method is called by PlayerUI
    public void InitializeCharacterActions(CharacterActions actions)
    {
        Unsubscribe(); // Memory-leak safeguard

        _characterActions = actions;

        if (_characterActions != null)
        {
            _characterActions.OnActionStarted += HandleActionStarted;
            _characterActions.OnActionFinished += HandleActionEnded;
        }

        // Hidden by default at startup
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_characterActions != null)
        {
            _fillImage.fillAmount = _characterActions.GetActionProgress();
        }
    }

    private void HandleActionStarted(CharacterAction action)
    {
        // Become visible only when the action starts
        gameObject.SetActive(true);
        
        if (_actionNameText != null)
            _actionNameText.text = action.ActionName.Replace("Character", "");
    }

    private void HandleActionEnded()
    {
        // Hide completely
        gameObject.SetActive(false);
    }

    private void Unsubscribe()
    {
        if (_characterActions != null)
        {
            _characterActions.OnActionStarted -= HandleActionStarted;
            _characterActions.OnActionFinished -= HandleActionEnded;
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}
