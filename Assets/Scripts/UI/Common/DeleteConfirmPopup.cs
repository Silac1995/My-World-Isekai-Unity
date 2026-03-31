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
    private bool _listenersAdded;

    private void EnsureListeners()
    {
        if (_listenersAdded) return;
        _listenersAdded = true;

        if (_yesButton != null)
            _yesButton.onClick.AddListener(OnYesClicked);

        if (_noButton != null)
            _noButton.onClick.AddListener(OnNoClicked);
    }

    private void Awake()
    {
        EnsureListeners();
    }

    public void Show(string name, Action onConfirm)
    {
        EnsureListeners();
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
