using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DeleteConfirmPopup : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _messageText;
    [SerializeField] private Button _yesButton;
    [SerializeField] private Button _noButton;

    private Action _onConfirm;

    private void Awake()
    {
        gameObject.SetActive(false);

        if (_yesButton != null)
            _yesButton.onClick.AddListener(OnYesClicked);

        if (_noButton != null)
            _noButton.onClick.AddListener(OnNoClicked);
    }

    public void Show(string name, Action onConfirm)
    {
        _onConfirm = onConfirm;

        if (_messageText != null)
            _messageText.text = $"Are you sure you want to delete \"{name}\"?";

        gameObject.SetActive(true);
    }

    private void OnYesClicked()
    {
        _onConfirm?.Invoke();
        _onConfirm = null;
        gameObject.SetActive(false);
    }

    private void OnNoClicked()
    {
        _onConfirm = null;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_yesButton != null)
            _yesButton.onClick.RemoveListener(OnYesClicked);

        if (_noButton != null)
            _noButton.onClick.RemoveListener(OnNoClicked);
    }
}
