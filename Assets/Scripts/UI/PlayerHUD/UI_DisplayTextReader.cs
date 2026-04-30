using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player-facing reader for <see cref="DisplayTextFurniture"/>. Singleton-on-demand: first
/// call to <see cref="Show"/> instantiates the prefab from
/// <c>Resources/UI/UI_DisplayTextReader</c> under the active Canvas, then re-uses that
/// instance for every subsequent open. Closes via Close button, outside-click overlay, or
/// the ESC key.
///
/// The reader is purely informative — it displays the sign's title (parent building name)
/// and body text. Job applications are not initiated from the sign: both player and NPC
/// applicants must walk to the boss in person and use the canonical
/// <see cref="InteractionAskForJob"/> path (see CharacterJob and the hold-E hiring menu).
/// This design was confirmed in the 2026-04-30 Help Wanted refinement.
///
/// Rule #26: All UI timing (none here at the moment) MUST use unscaled time so the reader is
/// usable when the GameSpeedController is paused or at Giga Speed.
/// </summary>
public class UI_DisplayTextReader : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/UI_DisplayTextReader";
    private static UI_DisplayTextReader _instance;

    [Header("Layout")]
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _bodyLabel;

    [Header("Dismiss")]
    [SerializeField] private Button _closeButton;
    [Tooltip("Full-screen invisible button behind the content panel — outside-click closes the reader.")]
    [SerializeField] private Button _dismissOverlay;

    private DisplayTextFurniture _currentSign;
    private CommercialBuilding _currentBuilding;

    /// <summary>
    /// Open (or re-open) the reader for <paramref name="sign"/>. Lazy-instantiates the
    /// singleton instance on first call. Safe to call repeatedly — just rebinds to the new
    /// sign and refreshes the UI.
    /// </summary>
    public static void Show(DisplayTextFurniture sign)
    {
        if (sign == null) return;

        if (_instance == null)
        {
            try
            {
                var prefab = Resources.Load<UI_DisplayTextReader>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[UI_DisplayTextReader] No prefab found at Resources/{PrefabResourcePath}. Did you create the prefab in Step 8?");
                    return;
                }

                var canvas = Object.FindFirstObjectByType<Canvas>();
                _instance = Instantiate(prefab, canvas != null ? canvas.transform : null);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return;
            }
        }

        _instance.ShowInternal(sign);
    }

    private void Awake()
    {
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_dismissOverlay != null) _dismissOverlay.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (_dismissOverlay != null) _dismissOverlay.onClick.RemoveListener(Close);

        // Drop the static instance reference so a fresh-loaded scene gets a clean re-spawn
        // on the next Show() call.
        if (_instance == this) _instance = null;
    }

    private void ShowInternal(DisplayTextFurniture sign)
    {
        _currentSign = sign;
        _currentBuilding = sign.GetComponentInParent<CommercialBuilding>();

        string title = _currentBuilding != null ? _currentBuilding.BuildingName : "Sign";
        if (_titleLabel != null) _titleLabel.text = title;
        if (_bodyLabel != null) _bodyLabel.text = sign.DisplayText;

        gameObject.SetActive(true);
    }

    private void Update()
    {
        // Local input only — there's only ever one local reader instance, so the standard
        // PlayerController-owns-input rule (#33) doesn't apply: this is a UI-internal escape
        // that targets the panel itself, not a player-character action.
        if (gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void Close()
    {
        gameObject.SetActive(false);
        _currentSign = null;
        _currentBuilding = null;
    }
}
