using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_Action_ProgressBar : MonoBehaviour
{
    private CharacterActions _characterActions;

    [Header("UI References")]
    [SerializeField] private Image _fillImage;
    [SerializeField] private TextMeshProUGUI _actionNameText;

    // Cette m?thode est appel?e par PlayerUI
    public void InitializeCharacterActions(CharacterActions actions)
    {
        Unsubscribe(); // S?curit? fuite m?moire

        _characterActions = actions;

        if (_characterActions != null)
        {
            _characterActions.OnActionStarted += HandleActionStarted;
            _characterActions.OnActionFinished += HandleActionEnded;
        }

        // On se cache par d?faut au d?marrage
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
        // On devient visible seulement quand l'action commence
        gameObject.SetActive(true);
        
        if (_actionNameText != null)
            _actionNameText.text = action.GetType().Name.Replace("Character", "");
    }

    private void HandleActionEnded()
    {
        // On dispara?t compl?tement
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
