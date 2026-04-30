using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// Owner-only management panel for a <see cref="CommercialBuilding"/>'s hiring state.
/// Opened via the building's interaction menu (the "Manage Hiring..." entry added to
/// <see cref="CharacterJob.GetInteractionOptions"/>). Player owners use this to:
/// - Toggle hiring open/closed (TryOpenHiring / TryCloseHiring).
/// - Write custom text to the Help Wanted sign (TrySetDisplayText on _helpWantedFurniture).
///
/// Custom sign text is overwritten when hiring re-opens (per spec §15.8 Q15.1) — the hint
/// label below the input field calls this out so owners aren't surprised.
///
/// Auto-refreshes via <see cref="CommercialBuilding.OnHiringStateChanged"/> subscription so
/// RPC-driven flips propagate visually within one frame.
///
/// Singleton-on-demand pattern: first <see cref="Show"/> call instantiates the prefab from
/// <c>Resources/UI/UI_OwnerHiringPanel</c>; subsequent calls re-use the instance and just
/// rebind to the new building. Mirrors the pattern used by
/// <see cref="UI_DisplayTextReader"/>.
///
/// Rule #26: panel input checks (ESC) use unscaled-time-safe Input polling — UI must remain
/// usable when the GameSpeedController is paused or running at Giga Speed.
/// </summary>
public class UI_OwnerHiringPanel : MonoBehaviour
{
    private const string PrefabResourcePath = "UI/UI_OwnerHiringPanel";
    private static UI_OwnerHiringPanel _instance;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private TextMeshProUGUI _statusLabel;

    [Header("Job List")]
    [Tooltip("Parent transform under which job-row instances are spawned. Usually a ScrollView Content.")]
    [SerializeField] private Transform _jobListRoot;
    [Tooltip("Row prefab — must contain a TextMeshProUGUI in itself or a child for the row label.")]
    [SerializeField] private GameObject _jobRowPrefab;

    [Header("Hiring Toggle")]
    [SerializeField] private Button _toggleHiringButton;
    [SerializeField] private TextMeshProUGUI _toggleHiringLabel;

    [Header("Sign Edit")]
    [SerializeField] private TMP_InputField _customTextInput;
    [SerializeField] private Button _submitTextButton;
    [SerializeField] private TextMeshProUGUI _customTextHint;

    [Header("Close")]
    [SerializeField] private Button _closeButton;
    [Tooltip("Full-screen invisible button behind the content panel — outside-click closes the panel.")]
    [SerializeField] private Button _dismissOverlay;

    private CommercialBuilding _building;
    private readonly List<GameObject> _spawnedRows = new List<GameObject>(8);

    /// <summary>
    /// Open (or re-open) the panel for <paramref name="building"/>. Lazy-instantiates the
    /// singleton instance on first call. Safe to call repeatedly — just rebinds to the new
    /// building, refreshes the UI, and re-installs the OnHiringStateChanged subscription.
    /// </summary>
    public static void Show(CommercialBuilding building)
    {
        if (building == null) return;
        if (_instance == null)
        {
            try
            {
                var prefab = Resources.Load<UI_OwnerHiringPanel>(PrefabResourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning($"[UI_OwnerHiringPanel] No prefab found at Resources/{PrefabResourcePath}.");
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
        _instance.ShowInternal(building);
    }

    private void Awake()
    {
        if (_toggleHiringButton != null) _toggleHiringButton.onClick.AddListener(OnToggleHiring);
        if (_submitTextButton != null) _submitTextButton.onClick.AddListener(OnSubmitText);
        if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        if (_dismissOverlay != null) _dismissOverlay.onClick.AddListener(Close);
    }

    private void OnDestroy()
    {
        if (_toggleHiringButton != null) _toggleHiringButton.onClick.RemoveListener(OnToggleHiring);
        if (_submitTextButton != null) _submitTextButton.onClick.RemoveListener(OnSubmitText);
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (_dismissOverlay != null) _dismissOverlay.onClick.RemoveListener(Close);

        // Make sure we don't leak the OnHiringStateChanged subscription.
        if (_building != null) _building.OnHiringStateChanged -= HandleHiringChanged;

        if (_instance == this) _instance = null;
    }

    private void ShowInternal(CommercialBuilding building)
    {
        // If we were viewing a different building, drop the old subscription first.
        if (_building != null && _building != building)
            _building.OnHiringStateChanged -= HandleHiringChanged;

        _building = building;
        if (_titleLabel != null)
            _titleLabel.text = $"Manage Hiring — {building.BuildingName}";

        // Re-installable subscription: -= followed by += is safe even if not previously bound.
        _building.OnHiringStateChanged -= HandleHiringChanged;
        _building.OnHiringStateChanged += HandleHiringChanged;

        Refresh();
        gameObject.SetActive(true);
    }

    private void Update()
    {
        // Local UI dismissal only — does not target the player character, so it's exempt
        // from rule #33 (PlayerController-owned input).
        if (gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    private void HandleHiringChanged(bool _) => Refresh();

    private void Refresh()
    {
        if (_building == null) return;

        if (_statusLabel != null)
            _statusLabel.text = _building.IsHiring
                ? "Currently Hiring: <color=#56C26B>Yes</color>"
                : "Currently Hiring: <color=#C25656>No</color>";

        if (_toggleHiringLabel != null)
            _toggleHiringLabel.text = _building.IsHiring ? "Close Hiring" : "Open Hiring";

        bool hasSign = _building.HelpWantedSign != null;
        if (_customTextInput != null) _customTextInput.interactable = hasSign;
        if (_submitTextButton != null) _submitTextButton.interactable = hasSign;
        if (_customTextHint != null)
            _customTextHint.text = hasSign
                ? "Custom text resets when hiring is reopened."
                : "(No Help Wanted sign assigned to this building.)";

        // Rebuild job list rows. Cheap — typically 1-4 rows.
        for (int i = 0; i < _spawnedRows.Count; i++)
        {
            if (_spawnedRows[i] != null) Destroy(_spawnedRows[i]);
        }
        _spawnedRows.Clear();

        if (_jobRowPrefab == null || _jobListRoot == null) return;

        var allJobs = _building.Jobs;
        if (allJobs == null) return;
        for (int i = 0; i < allJobs.Count; i++)
        {
            var job = allJobs[i];
            if (job == null) continue;
            var row = Instantiate(_jobRowPrefab, _jobListRoot);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                string status = job.IsAssigned
                    ? (job.Worker != null ? job.Worker.CharacterName : "(filled)")
                    : "vacant";
                string title = string.IsNullOrEmpty(job.JobTitle) ? "Worker" : job.JobTitle;
                label.text = $"{title} — {status}";
            }
            _spawnedRows.Add(row);
        }
    }

    private void OnToggleHiring()
    {
        if (_building == null) return;
        var localPlayer = ResolveLocalPlayerCharacter();
        if (localPlayer == null)
        {
            Debug.LogWarning("[UI_OwnerHiringPanel] Toggle rejected — could not resolve local player Character.");
            return;
        }

        if (_building.IsHiring) _building.TryCloseHiring(localPlayer);
        else _building.TryOpenHiring(localPlayer);
        // Visual refresh fires automatically via OnHiringStateChanged subscription.
    }

    private void OnSubmitText()
    {
        if (_building == null) return;
        var sign = _building.HelpWantedSign;
        if (sign == null) return;

        var localPlayer = ResolveLocalPlayerCharacter();
        if (localPlayer == null)
        {
            Debug.LogWarning("[UI_OwnerHiringPanel] Submit text rejected — could not resolve local player Character.");
            return;
        }

        string text = _customTextInput != null ? _customTextInput.text : string.Empty;
        sign.TrySetDisplayText(localPlayer, text);
        // No immediate UI feedback — _displayText replicates via NetworkVariable and the
        // sign's own visual refresh updates automatically. Owner can verify by walking up
        // to the sign and reading it.
    }

    private void Close()
    {
        if (_building != null)
        {
            _building.OnHiringStateChanged -= HandleHiringChanged;
            _building = null;
        }
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Canonical local-player Character resolver — same pattern used by
    /// <see cref="UI_DisplayTextReader.ResolveLocalPlayerCharacter"/>. Returns null if
    /// NetworkManager isn't up yet, no LocalClient is bound, or the player NetworkObject
    /// hasn't spawned.
    /// </summary>
    private static Character ResolveLocalPlayerCharacter()
    {
        try
        {
            if (NetworkManager.Singleton == null) return null;
            var localClient = NetworkManager.Singleton.LocalClient;
            if (localClient == null || localClient.PlayerObject == null) return null;
            return localClient.PlayerObject.GetComponent<Character>();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }
}
