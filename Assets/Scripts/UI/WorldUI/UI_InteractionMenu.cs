using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UI_InteractionMenu : MonoBehaviour
{
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Transform buttonsContainer;
    [SerializeField] private Image _timerBar;

    private readonly List<Button> _pool = new List<Button>();

    public void Initialize(List<InteractableObject.InteractionOption> options)
    {
        // 1. Deactivate all pooled buttons
        foreach (var btn in _pool)
        {
            if (btn != null) btn.gameObject.SetActive(false);
        }

        // 2. Grow pool if needed, reuse existing buttons
        for (int i = 0; i < options.Count; i++)
        {
            Button btn;
            if (i < _pool.Count)
            {
                btn = _pool[i];
            }
            else
            {
                GameObject btnObj = Instantiate(buttonPrefab, buttonsContainer);
                btn = btnObj.GetComponent<Button>();
                _pool.Add(btn);
            }

            btn.onClick.RemoveAllListeners();

            TextMeshProUGUI txt = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null)
                txt.text = options[i].Name;

            // Capture for closure
            var action = options[i].Action;
            btn.onClick.AddListener(() =>
            {
                action?.Invoke();
                // Lock all buttons immediately — one action per turn
                SetOptionsInteractable(false);
            });

            btn.gameObject.SetActive(true);
        }

        // Default: locked until the player's turn starts
        SetOptionsInteractable(false);
        UpdateTimer(1f);
    }

    public bool IsLocked { get; private set; }

    /// <summary>
    /// Enable or disable all ACTIVE option buttons (locked = not the player's turn).
    /// </summary>
    public void SetOptionsInteractable(bool interactable)
    {
        IsLocked = !interactable;
        foreach (var btn in _pool)
        {
            if (btn != null && btn.gameObject.activeSelf)
                btn.interactable = interactable;
        }
    }

    /// <summary>
    /// Update the timer bar fill amount. 1.0 = full, 0.0 = empty (time ran out).
    /// </summary>
    public void UpdateTimer(float normalizedValue)
    {
        if (_timerBar != null)
        {
            _timerBar.fillAmount = Mathf.Clamp01(normalizedValue);
        }
    }

    public void CloseMenu()
    {
        foreach (var btn in _pool)
        {
            if (btn != null) btn.gameObject.SetActive(false);
        }
        gameObject.SetActive(false);
    }
}
