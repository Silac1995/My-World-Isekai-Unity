using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using MWI.Ambition;
using MWI.WorldSystem;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// DEV-ONLY city-founding mutator surface for the Character inspector. Lets the host
/// drive the Plan 4 city-founding loop (create community → ambition → AB placement →
/// treasury credit → tier-up → drifter migration → join requests) without authoring
/// content or waiting for the natural in-game cadence.
///
/// Lives as a sibling to the 10 stock <see cref="CharacterSubTab"/>s. Inherits the
/// base class but overrides <see cref="Refresh(Character)"/> directly to render
/// programmatic UGUI widgets instead of the text-only <c>RenderContent</c> path
/// (the base's _content TMP_Text is intentionally left unwired on this prefab —
/// the override never touches it).
///
/// Authority model: every mutator either calls a public POCO method on
/// <see cref="CharacterCommunity"/> / <see cref="Community"/> directly (safe because
/// dev mode is host-only, so we are always on the server), or routes through a
/// <c>DevForce*</c> wrapper on <see cref="CommercialBuilding"/> / <see cref="AdministrativeBuilding"/> /
/// <see cref="MWI.Time.TimeManager"/> that asserts host + DevMode and emits an audit log.
///
/// Sections (top to bottom):
///   1. DEV banner
///   2. Citizenship + community status header (read-only)
///   3. Create Community         — visible when CurrentCommunity == null
///   4. Assign Ambition_FoundACity
///   5. Community read-only panel — visible when CurrentCommunity != null
///   6. Force-Promote Community  — visible when CurrentCommunity != null
///   7. Grant Treasury 1000      — visible when CurrentCommunity != null && AB != null
///   8. Submit Join Request      — visible when target has no community + a chartered AB on map
///   9. Time control (Force NewDay) — universal
/// </summary>
public sealed class CharacterCityFoundingSubTab : CharacterSubTab
{
    /// <summary>
    /// Where widgets get parented. Wired in the prefab to the inner
    /// <c>Viewport/Content</c> RectTransform (which carries the VerticalLayoutGroup +
    /// ContentSizeFitter inherited from the duplicated sub-tab base). Falls back to
    /// the script's own transform when null so a designer can also use this sub-tab
    /// on a flat (non-scrolling) panel.
    /// </summary>
    [SerializeField] private RectTransform _widgetRoot;

    private Character _bound;
    private readonly List<GameObject> _spawnedWidgets = new();

    // Cached transient input values (preserve between rebuilds when the user typed
    // something then triggered a rebuild via a sibling button).
    private string _treasuryAmountInput = "1000";
    private string _newDayCountInput = "1";

    private Transform WidgetParent => _widgetRoot != null ? (Transform)_widgetRoot : transform;

    /// <summary>Required by the abstract base — this sub-tab renders widgets, not text.</summary>
    protected override string RenderContent(Character c) => string.Empty;

    public override void Refresh(Character c)
    {
        if (c == null) { Clear(); return; }
        if (_bound != c)
        {
            _bound = c;
            RebuildAll();
        }
        // Live state (treasury / member count) refreshes only on rebuild — the user
        // clicks a button to trigger a fresh read. A "Refresh" button at the top of
        // RebuildAll gives an explicit refresh path without per-frame allocation.
    }

    public override void Clear()
    {
        _bound = null;
        ClearWidgets();
        base.Clear();
    }

    private void ClearWidgets()
    {
        for (int i = 0; i < _spawnedWidgets.Count; i++)
            if (_spawnedWidgets[i] != null) Destroy(_spawnedWidgets[i]);
        _spawnedWidgets.Clear();
    }

    private void RebuildAll()
    {
        ClearWidgets();
        if (_bound == null) return;

        BuildBanner();
        BuildRefreshButton();
        BuildStatusHeader(_bound);
        BuildCreateCommunitySection(_bound);
        BuildAssignAmbitionSection(_bound);
        BuildCommunityReadoutSection(_bound);
        BuildForcePromoteSection(_bound);
        BuildGrantTreasurySection(_bound);
        BuildSubmitJoinRequestSection(_bound);
        BuildTimeControlSection();
    }

    // ─── Feature 1: Create Community ─────────────────────────────────────

    /// <summary>
    /// Shows a "Create Community" button when the target character is not currently
    /// a member of any community. Server-side path is
    /// <see cref="CharacterCommunity.CreateCommunity(string)"/> — same call the
    /// production <c>Task_CreateCommunity</c> uses, so the founder auto-receives the
    /// AdministrativeBuilding blueprint (Plan 4a). Dev mode is host-only, so we are
    /// always on the server when this button fires.
    /// </summary>
    private void BuildCreateCommunitySection(Character c)
    {
        if (c == null || c.CharacterCommunity == null) return;
        if (c.CharacterCommunity.CurrentCommunity != null) return;

        MakeHeader("Create Community");
        MakeLabel("<color=grey>Founds a new SmallGroup community led by this character. Auto-grants the AdministrativeBuilding blueprint to the founder.</color>");

        var row = MakeRow();
        var nameInput = MakeInput("community name", "DebugCity", row.transform, minWidth: 160);
        MakeButton("[DEV] Create Community", () =>
        {
            if (c == null || c.CharacterCommunity == null) return;
            string communityName = string.IsNullOrWhiteSpace(nameInput.text) ? "DebugCity" : nameInput.text.Trim();
            try
            {
                c.CharacterCommunity.CreateCommunity(communityName);
                Debug.Log($"<color=magenta>[DevMode]</color> CreateCommunity('{communityName}') invoked on '{c.CharacterName}'.");
            }
            catch (Exception e) { Debug.LogException(e); }
            RebuildAll();
        }, row.transform);
    }

    // ─── Sections ────────────────────────────────────────────────────────

    private void BuildBanner()
    {
        var go = new GameObject("DEV_Banner", typeof(RectTransform));
        go.transform.SetParent(WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 28; le.preferredHeight = 28;
        var img = go.AddComponent<Image>(); img.color = new Color(0.6f, 0.2f, 0.2f, 1f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8, 0); lrt.offsetMax = new Vector2(-8, 0);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "[DEV] City Founding — host-only, bypasses production gates";
        tmp.fontSize = 13; tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white; tmp.raycastTarget = false;

        _spawnedWidgets.Add(go);
    }

    private void BuildRefreshButton()
    {
        var row = MakeRow();
        MakeButton("⟳ Refresh", RebuildAll, row.transform);
        MakeLabel("<color=grey>(rebuild widgets from live state)</color>", row.transform);
    }

    private void BuildStatusHeader(Character c)
    {
        if (c == null) return;
        MakeHeader("Status");

        var cc = c.CharacterCommunity;
        if (cc == null)
        {
            MakeLabel("<color=#FF6464>CharacterCommunity component missing.</color>");
            return;
        }

        var current = cc.CurrentCommunity;
        var citizenship = cc.Citizenship;

        var sb = new StringBuilder(160);
        sb.Append("<color=#FFD27A>Character:</color> ").Append(c.CharacterName ?? "—");
        sb.Append("    <color=#FFD27A>CurrentCommunity:</color> ");
        sb.Append(current != null
            ? $"<color=#64FF64>{current.communityName}</color> ({(current.CurrentTier != null ? current.CurrentTier.DisplayName : current.level.ToString())})"
            : "<color=grey>none</color>");
        sb.Append("    <color=#FFD27A>Citizenship:</color> ");
        sb.Append(citizenship != null
            ? $"<color=#64FF64>{citizenship.communityName}</color>"
            : "<color=grey>none</color>");
        MakeLabel(sb.ToString());
    }

    // ─── Feature 2: Assign Ambition_FoundACity ───────────────────────────

    /// <summary>
    /// Loads <c>Resources/Data/Ambitions/Ambition_FoundACity</c> and assigns it to
    /// the target character via <see cref="CharacterAmbition.SetAmbition"/> — the
    /// same entry point production callers use. Dev mode is host-only so we are
    /// always on the server; SetAmbition short-circuits its ServerRpc and runs
    /// <c>DoSetAmbition</c> directly. The BT picks up the new ambition on its next
    /// tick (no manual nudge required).
    /// </summary>
    private void BuildAssignAmbitionSection(Character c)
    {
        if (c == null) return;
        var ambitions = c.CharacterAmbition;
        if (ambitions == null) return;

        MakeHeader("Ambition_FoundACity");

        var current = ambitions.Current;
        string currentLabel = current != null && current.SO != null
            ? $"<color=#64FF64>active:</color> {current.SO.name} (step {current.CurrentStepIndex + 1}/{current.TotalSteps})"
            : "<color=grey>none</color>";
        MakeLabel($"Current ambition: {currentLabel}");

        var row = MakeRow();
        MakeButton("[DEV] Assign Ambition_FoundACity", () =>
        {
            var so = Resources.Load<AmbitionSO>("Data/Ambitions/Ambition_FoundACity");
            if (so == null)
            {
                Debug.LogWarning("<color=magenta>[DevMode]</color> Could not load Ambition_FoundACity at Resources/Data/Ambitions/Ambition_FoundACity. Asset path may have moved.");
                return;
            }
            try
            {
                ambitions.SetAmbition(so);
                Debug.Log($"<color=magenta>[DevMode]</color> SetAmbition(Ambition_FoundACity) invoked on '{c.CharacterName}'.");
            }
            catch (Exception e) { Debug.LogException(e); }
            RebuildAll();
        }, row.transform, minWidth: 220);

        if (current != null)
        {
            MakeButton("[DEV] Clear Ambition", () =>
            {
                try { ambitions.ClearAmbition(); }
                catch (Exception e) { Debug.LogException(e); }
                RebuildAll();
            }, row.transform);
        }
    }

    // ─── Feature 3: Read-only community panel ─────────────────────────────

    /// <summary>
    /// Renders the live state of the target's <c>CurrentCommunity</c> — name,
    /// level, IsChartered, member/leader counts, AB reference + AB treasury
    /// balance in CurrencyId.Default. Treasury read goes through
    /// <see cref="CommercialBuilding.GetTreasuryBalance"/> (sums every Treasury-role
    /// safe). No mutators here; this section is purely diagnostic.
    /// </summary>
    private void BuildCommunityReadoutSection(Character c)
    {
        if (c == null || c.CharacterCommunity == null) return;
        var community = c.CharacterCommunity.CurrentCommunity;
        if (community == null) return;

        MakeHeader("Community");

        int memberCount  = community.members  != null ? community.members.Count  : 0;
        int leaderCount  = community.leaders  != null ? community.leaders.Count  : 0;
        var ab           = community.AdministrativeBuilding;
        int treasury     = ab != null ? ab.GetTreasuryBalance(MWI.Economy.CurrencyId.Default) : 0;
        string charteredColor = community.IsChartered ? "#64FF64" : "#FF6464";
        string abLabel = ab != null
            ? $"{ab.BuildingName} <color=grey>(id={ab.BuildingId}, construction={(ab.IsUnderConstruction ? "in progress" : "complete")})</color>"
            : "<color=grey>none — community not yet chartered</color>";

        MakeLabel($"<color=#FFD27A>Name:</color> {community.communityName}");
        MakeLabel($"<color=#FFD27A>Level:</color> {(community.CurrentTier != null ? $"{community.CurrentTier.DisplayName} <color=grey>(order {community.CurrentTier.Order}, id '{community.CurrentTier.TierId}')</color>" : community.level.ToString() + " <color=grey>(no tier SO)</color>")}");
        MakeLabel($"<color=#FFD27A>IsChartered:</color> <color={charteredColor}>{community.IsChartered}</color>");
        MakeLabel($"<color=#FFD27A>Members:</color> {memberCount}    <color=#FFD27A>Leaders:</color> {leaderCount} (primary: {community.PrimaryLeader?.CharacterName ?? "—"})");
        MakeLabel($"<color=#FFD27A>AdministrativeBuilding:</color> {abLabel}");
        MakeLabel($"<color=#FFD27A>Treasury (Default):</color> <color=#FFD27A>{treasury}</color> coin");
    }

    // ─── Feature 4: Force-Promote Community ──────────────────────────────

    /// <summary>
    /// Bypasses <see cref="Community.TryPromoteLevel"/>'s population / treasury /
    /// required-building gates and forces the community's level up (or down) one
    /// tier via <see cref="AdministrativeBuilding.DevForceChangeCommunityLevel"/>.
    /// Requires the community to be chartered (an AB exists) — production
    /// promotion requires the same anchor.
    /// </summary>
    private void BuildForcePromoteSection(Character c)
    {
        if (c == null || c.CharacterCommunity == null) return;
        var community = c.CharacterCommunity.CurrentCommunity;
        if (community == null) return;

        MakeHeader("Force-Promote Community");

        var ab = community.AdministrativeBuilding;
        if (ab == null)
        {
            MakeLabel("<color=grey>Requires a chartered AdministrativeBuilding. Place an AB first (Ambition_FoundACity step 2).</color>");
            return;
        }

        var row = MakeRow();
        MakeLabel($"<color=#FFD27A>Current:</color> {(community.CurrentTier != null ? community.CurrentTier.DisplayName : community.level.ToString())}", row.transform);
        MakeButton("[DEV] −1 Tier", () =>
        {
            try { ab.DevForceChangeCommunityLevel(-1); }
            catch (Exception e) { Debug.LogException(e); }
            RebuildAll();
        }, row.transform);
        MakeButton("[DEV] +1 Tier", () =>
        {
            try { ab.DevForceChangeCommunityLevel(+1); }
            catch (Exception e) { Debug.LogException(e); }
            RebuildAll();
        }, row.transform);
    }

    // ─── Feature 5: Grant Treasury ────────────────────────────────────────

    /// <summary>
    /// Credits a designer-supplied amount (default 1000) into the AB's Treasury safes
    /// via <see cref="CommercialBuilding.DevForceCreditTreasury"/>. Currency resolves
    /// to the enclosing map's <c>NativeCurrency</c> with a <c>Default</c> fallback.
    /// Requires the community to be chartered (an AB exists) and the AB to have at
    /// least one Treasury-role safe (otherwise <c>CreditTreasury</c> logs and skips).
    /// </summary>
    private void BuildGrantTreasurySection(Character c)
    {
        if (c == null || c.CharacterCommunity == null) return;
        var community = c.CharacterCommunity.CurrentCommunity;
        if (community == null) return;
        var ab = community.AdministrativeBuilding;
        if (ab == null) return;

        MakeHeader("Grant Treasury");
        MakeLabel("<color=grey>Credits the AB's Treasury-role safe(s) directly. Currency = enclosing map's NativeCurrency (CurrencyId.Default fallback).</color>");

        var row = MakeRow();
        var amountInput = MakeInput("amount", _treasuryAmountInput, row.transform, minWidth: 100);
        MakeButton("[DEV] Grant Treasury", () =>
        {
            if (!int.TryParse(amountInput.text, out int amount) || amount <= 0)
            {
                Debug.LogWarning($"<color=magenta>[DevMode]</color> Grant Treasury: invalid amount '{amountInput.text}'.");
                return;
            }
            _treasuryAmountInput = amountInput.text;
            try { ab.DevForceCreditTreasury(amount); }
            catch (Exception e) { Debug.LogException(e); }
            RebuildAll();
        }, row.transform, minWidth: 160);
    }

    // ─── Feature 6: Time control ──────────────────────────────────────────

    /// <summary>
    /// Universal — visible regardless of the inspected character's community state.
    /// Pumps <see cref="MWI.Time.TimeManager.OnNewDay"/> N times via
    /// <see cref="MWI.Time.TimeManager.DevForceNewDay"/>, which is the trigger
    /// <see cref="DrifterMigrationSystem"/> listens on. One click ≙ one drifter
    /// roll (subject to the system's own probability gates).
    ///
    /// Lives in this sub-tab (not a dedicated Time tab) because the only current
    /// caller is the city-founding loop iteration. Move it to a Time sub-tab if a
    /// second consumer appears.
    /// </summary>
    private void BuildTimeControlSection()
    {
        MakeHeader("Time");

        var tm = MWI.Time.TimeManager.Instance;
        if (tm == null)
        {
            MakeLabel("<color=grey>(no TimeManager in scene)</color>");
            return;
        }

        MakeLabel($"<color=#FFD27A>Day:</color> {tm.CurrentDay}    <color=#FFD27A>Hour:</color> {tm.CurrentHour:00}:{tm.CurrentMinute:00}    <color=#FFD27A>Phase:</color> {tm.CurrentPhase}");

        var row = MakeRow();
        var countInput = MakeInput("days", _newDayCountInput, row.transform, minWidth: 60);
        MakeButton("[DEV] Force NewDay", () =>
        {
            if (!int.TryParse(countInput.text, out int count) || count <= 0) count = 1;
            _newDayCountInput = countInput.text;
            try { tm.DevForceNewDay(count); }
            catch (Exception e) { Debug.LogException(e); }
            RebuildAll();
        }, row.transform, minWidth: 160);
        MakeLabel("<color=grey>fires OnNewDay N times — drives DrifterMigrationSystem + ambition daily tasks.</color>", row.transform);
    }

    // ─── Feature 7: Submit Join Request ───────────────────────────────────

    /// <summary>
    /// Visible when the inspected character has no <c>CurrentCommunity</c> and no
    /// <c>Citizenship</c>, and at least one chartered, fully-constructed
    /// <see cref="AdministrativeBuilding"/> exists in the scene. One button per
    /// candidate AB; click routes the join request through the canonical
    /// <see cref="AdministrativeBuilding.SubmitJoinRequestServerRpc"/> using the
    /// character's <c>NetworkObject.NetworkObjectId</c> — identical to the path
    /// drifters take when they interact with the JoinRequestDesk.
    /// </summary>
    private void BuildSubmitJoinRequestSection(Character c)
    {
        if (c == null || c.CharacterCommunity == null) return;
        if (c.CharacterCommunity.CurrentCommunity != null) return;
        if (c.CharacterCommunity.Citizenship != null) return;
        if (c.NetworkObject == null) return; // applicant must be a NetworkObject for SubmitJoinRequestServerRpc

        // Scan the scene for chartered ABs whose construction is complete.
        var abs = UnityEngine.Object.FindObjectsByType<AdministrativeBuilding>(FindObjectsSortMode.None);
        var candidates = new List<AdministrativeBuilding>();
        for (int i = 0; i < abs.Length; i++)
        {
            var ab = abs[i];
            if (ab == null || ab.OwnerCommunity == null) continue;
            if (ab.IsUnderConstruction) continue;
            candidates.Add(ab);
        }

        if (candidates.Count == 0) return;

        MakeHeader("Submit Join Request");
        MakeLabel("<color=grey>Routes through AdministrativeBuilding.SubmitJoinRequestServerRpc — same path as drifters interacting with the JoinRequestDesk.</color>");

        ulong applicantNetId = c.NetworkObject.NetworkObjectId;
        for (int i = 0; i < candidates.Count; i++)
        {
            var ab = candidates[i];
            var row = MakeRow();
            string tierLabel = ab.OwnerCommunity.CurrentTier != null
                ? ab.OwnerCommunity.CurrentTier.DisplayName
                : ab.OwnerCommunity.level.ToString();
            string label = $"{ab.OwnerCommunity.communityName} <color=grey>({tierLabel}, members={ab.OwnerCommunity.members.Count})</color>";
            MakeLabel(label, row.transform);
            var capturedAb = ab;
            MakeButton("[DEV] Submit Join Request", () =>
            {
                try { capturedAb.SubmitJoinRequestServerRpc(applicantNetId); }
                catch (Exception e) { Debug.LogException(e); }
                RebuildAll();
            }, row.transform, minWidth: 200);
        }
    }

    // ─── Widget helpers (programmatic UGUI, dev-only) ────────────────────

    private GameObject MakeRow(Transform parent = null)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 26;
        var hl = go.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
        hl.childControlWidth = true; hl.childControlHeight = true;
        hl.childAlignment = TextAnchor.MiddleLeft;
        _spawnedWidgets.Add(go);
        return go;
    }

    private GameObject MakeHeader(string text)
    {
        var go = MakeLabel($"<b>{text}</b>");
        var tmp = go.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
        {
            tmp.fontSize = 14;
            tmp.color = new Color(1f, 0.85f, 0.6f, 1f);
        }
        return go;
    }

    private GameObject MakeLabel(string text, Transform parent = null)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 22; le.flexibleWidth = 1;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = 12;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        tmp.raycastTarget = false; tmp.richText = true;
        _spawnedWidgets.Add(go);
        return go;
    }

    private Button MakeButton(string label, Action onClick, Transform parent = null, float minWidth = 100)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24; le.minWidth = minWidth;
        var img = go.AddComponent<Image>(); img.color = new Color(0.40f, 0.20f, 0.20f, 1f);
        var btn = go.AddComponent<Button>(); btn.targetGraphic = img;

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
        var ltmp = labelGO.AddComponent<TextMeshProUGUI>();
        ltmp.text = label; ltmp.alignment = TextAlignmentOptions.Center;
        ltmp.fontSize = 12; ltmp.color = Color.white; ltmp.raycastTarget = false;

        btn.onClick.AddListener(() =>
        {
            try { onClick?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        });
        _spawnedWidgets.Add(go);
        return btn;
    }

    private TMP_InputField MakeInput(string placeholder, string initialText = "", Transform parent = null, float minWidth = 100)
    {
        var go = new GameObject("Input", typeof(RectTransform));
        go.transform.SetParent(parent ?? WidgetParent, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24; le.minWidth = minWidth;
        var img = go.AddComponent<Image>(); img.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        var inp = go.AddComponent<TMP_InputField>(); inp.image = img;

        var textArea = new GameObject("Text", typeof(RectTransform));
        textArea.transform.SetParent(go.transform, worldPositionStays: false);
        var tart = textArea.GetComponent<RectTransform>();
        tart.anchorMin = Vector2.zero; tart.anchorMax = Vector2.one;
        tart.offsetMin = new Vector2(4, 2); tart.offsetMax = new Vector2(-4, -2);
        var ttmp = textArea.AddComponent<TextMeshProUGUI>();
        ttmp.fontSize = 12; ttmp.color = Color.white;
        ttmp.alignment = TextAlignmentOptions.MidlineLeft;
        ttmp.raycastTarget = false;
        inp.textComponent = ttmp;

        var phGO = new GameObject("Placeholder", typeof(RectTransform));
        phGO.transform.SetParent(textArea.transform, worldPositionStays: false);
        var phrt = phGO.GetComponent<RectTransform>();
        phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
        phrt.offsetMin = Vector2.zero; phrt.offsetMax = Vector2.zero;
        var phtmp = phGO.AddComponent<TextMeshProUGUI>();
        phtmp.text = placeholder; phtmp.fontSize = 12;
        phtmp.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        phtmp.alignment = TextAlignmentOptions.MidlineLeft;
        phtmp.raycastTarget = false;
        inp.placeholder = phtmp;

        if (!string.IsNullOrEmpty(initialText)) inp.SetTextWithoutNotify(initialText);

        _spawnedWidgets.Add(go);
        return inp;
    }
}
#endif
