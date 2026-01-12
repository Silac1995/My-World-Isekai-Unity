using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_Action_ProgressBar : MonoBehaviour
{
    private CharacterActions _characterActions;

    [Header("UI References")]
    [SerializeField] private Image _fillImage;
    [SerializeField] private TextMeshProUGUI _actionNameText;

    // Cette méthode est appelée par PlayerUI
    public void InitializeCharacterActions(CharacterActions actions)
    {
        Unsubscribe(); // Sécurité fuite mémoire

        _characterActions = actions;

        if (_characterActions != null)
        {
            _characterActions.OnActionStarted += HandleActionStarted;
            _characterActions.OnActionCanceled += HandleActionEnded;
        }

        // On se cache par défaut au démarrage
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
        // On disparaît complètement
        gameObject.SetActive(false);
    }

    private void Unsubscribe()
    {
        if (_characterActions != null)
        {
            _characterActions.OnActionStarted -= HandleActionStarted;
            _characterActions.OnActionCanceled -= HandleActionEnded;
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}