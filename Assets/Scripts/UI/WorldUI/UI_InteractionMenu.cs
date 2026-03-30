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
    private bool _lockAfterClick;

    /// <param name="lockByDefault">
    /// When true (default), all buttons start disabled and lock after each click
    /// — used by combat turn system where only one action per turn is allowed.
    /// When false, buttons respect InteractionOption.IsDisabled and stay clickable after use.
    /// </param>
    public void Initialize(List<InteractionOption> options, bool lockByDefault = true)
    {
        _lockAfterClick = lockByDefault;

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
            var optionName = options[i].Name;
            var toggleName = options[i].ToggleName;
            var label = txt;
            btn.onClick.AddListener(() =>
            {
                action?.Invoke();
                // Swap button text if ToggleName is set (e.g., Lock ↔ Unlock)
                if (!string.IsNullOrEmpty(toggleName) && label != null)
                    label.text = label.text == optionName ? toggleName : optionName;
                if (_lockAfterClick)
                    SetOptionsInteractable(false);
                else
                    CloseMenu(); // Non-combat menus close after any action
            });

            // Per-button disabled state
            btn.interactable = !options[i].IsDisabled;
            btn.gameObject.SetActive(true);
        }

        if (lockByDefault)
        {
            // Combat turn system: lock everything until the player's turn starts
            SetOptionsInteractable(false);
        }

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
