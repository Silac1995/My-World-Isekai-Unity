using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Player-facing reader for <see cref="DisplayTextFurniture"/>. Singleton-on-demand: first
/// call to <see cref="Show"/> instantiates the prefab from
/// <c>Resources/UI/UI_DisplayTextReader</c> under the active Canvas, then re-uses that
/// instance for every subsequent open. Closes via Close button, outside-click overlay, or
/// the ESC key.
///
/// If the displayed sign is the parent building's <see cref="CommercialBuilding.HelpWantedSign"/>
/// AND <see cref="CommercialBuilding.IsHiring"/> == true, the "Apply for a job" button is
/// shown. Clicking it routes through <see cref="CharacterJob.RequestJobApplicationServerRpc"/>
/// — the same client→server path used by the hold-E hiring menu (see CharacterJob change-log
/// 2026-04-24, wiki/systems/character-job.md). The ServerRpc re-validates ownership, index
/// range, !job.IsAssigned, and runs the Task 5 IsHiring gate inside InteractionAskForJob.
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

    [Header("Apply button")]
    [Tooltip("Root GameObject toggled active/inactive based on the help-wanted condition.")]
    [SerializeField] private GameObject _applyButton;
    [Tooltip("Button component on _applyButton — used to register the click listener.")]
    [SerializeField] private Button _applyButtonComponent;

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
        if (_applyButtonComponent != null) _applyButtonComponent.onClick.AddListener(OnApplyClicked);
    }

    private void OnDestroy()
    {
        if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
        if (_dismissOverlay != null) _dismissOverlay.onClick.RemoveListener(Close);
        if (_applyButtonComponent != null) _applyButtonComponent.onClick.RemoveListener(OnApplyClicked);

        // Drop the static instance reference so a fresh-loaded scene gets a clean re-spawn
        // on the next Show() call.
        if (_instance == this) _instance = null;
    }

    private void ShowInternal(DisplayTextFurniture sign)
    {
        _currentSign = sign;
        _currentBuilding = sign != null ? sign.GetComponentInParent<CommercialBuilding>() : null;

        bool isHelpWanted = _currentBuilding != null
            && _currentBuilding.HelpWantedSign == sign
            && _currentBuilding.IsHiring;

        string title = _currentBuilding != null ? _currentBuilding.BuildingName : "Sign";
        if (string.IsNullOrEmpty(title)) title = "Sign";

        if (_titleLabel != null) _titleLabel.text = title;
        if (_bodyLabel != null) _bodyLabel.text = sign != null ? sign.DisplayText : string.Empty;
        if (_applyButton != null) _applyButton.SetActive(isHelpWanted);

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

    private void OnApplyClicked()
    {
        ApplyForJobAtCurrentBuilding();
    }

    /// <summary>
    /// Validates the application is legal client-side (owner present, player has no job,
    /// vacancies remain), picks the first vacancy as a V1 default (multi-vacancy sub-menu is
    /// a Phase 2 follow-up), then routes the application through the canonical
    /// <see cref="CharacterJob.RequestJobApplicationServerRpc"/> path. Server re-validates
    /// every gate (ownership, index range, !IsAssigned, IsHiring via the Task 5 gate inside
    /// <see cref="InteractionAskForJob.CanExecute"/>).
    /// </summary>
    private void ApplyForJobAtCurrentBuilding()
    {
        if (_currentBuilding == null || !_currentBuilding.IsHiring) return;

        if (!_currentBuilding.HasOwner)
        {
            Debug.LogWarning("[UI_DisplayTextReader] Apply rejected — building has no Owner.");
            return;
        }

        Character localPlayer = ResolveLocalPlayerCharacter();
        if (localPlayer == null)
        {
            Debug.LogWarning("[UI_DisplayTextReader] Apply rejected — could not resolve local player Character.");
            return;
        }

        if (localPlayer.CharacterJob != null && localPlayer.CharacterJob.HasJob)
        {
            Debug.Log("[UI_DisplayTextReader] Apply rejected — player already has a job.");
            return;
        }

        var vacancies = _currentBuilding.GetVacantJobs();
        if (vacancies == null || vacancies.Count == 0)
        {
            Debug.Log("[UI_DisplayTextReader] Apply rejected — no vacancies remain.");
            return;
        }

        // V1: auto-pick the first vacancy. Multi-vacancy sub-menu is a Phase 2 follow-up.
        var job = vacancies[0];
        int stableIdx = _currentBuilding.GetJobStableIndex(job);
        if (stableIdx < 0)
        {
            Debug.LogWarning("[UI_DisplayTextReader] Apply rejected — picked job is not in the building's stable Jobs list.");
            return;
        }

        var owner = _currentBuilding.Owner;
        ulong ownerNetId = owner != null && owner.NetworkObject != null
            ? owner.NetworkObject.NetworkObjectId
            : 0;
        if (ownerNetId == 0)
        {
            Debug.LogWarning("[UI_DisplayTextReader] Apply rejected — owner has no NetworkObject id.");
            return;
        }

        if (localPlayer.CharacterJob == null)
        {
            Debug.LogWarning("[UI_DisplayTextReader] Apply rejected — local player has no CharacterJob component.");
            return;
        }

        // Same client→server path used by CharacterJob.OnJobEntryClicked for the hold-E
        // hiring menu (2026-04-24 work). The ServerRpc validates everything authoritatively.
        localPlayer.CharacterJob.RequestJobApplicationServerRpc(ownerNetId, stableIdx);
        Close();
    }

    /// <summary>
    /// Canonical local-player Character resolver — same pattern used by
    /// <c>UI_CharacterMapTrackerOverlay.FindLocalPlayerMapTracker</c> and
    /// <c>HUDSpeechBubbleLayer.LocalPlayerAnchor</c>. Returns null if NetworkManager isn't
    /// up yet (early-init), no LocalClient is bound, or the player NetworkObject hasn't
    /// spawned.
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
