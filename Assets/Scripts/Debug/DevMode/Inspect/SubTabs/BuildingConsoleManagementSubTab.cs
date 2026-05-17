using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// DEV-ONLY mutator surface for the Building inspector. Builds widgets at runtime
/// because the dev panel is host-only and uses programmatic UGUI throughout.
///
/// Every mutator either:
///   - Calls a server-only method that has no auth check (e.g. <c>SetOwner</c>,
///     <c>AssignWorker</c>, <c>RemoveWorker</c>, <c>AddToInventory</c>) directly —
///     this is safe because dev mode is host-only, so we are always on the server.
///   - Calls a <c>DevForce*</c> method on <see cref="CommercialBuilding"/> /
///     <see cref="ShopBuilding"/> that bypasses the owner / community-leader auth
///     gate that the production RPC enforces. <c>DevForce*</c> methods are wrapped
///     in <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c> and assert host + DevMode.
///
/// Sections (top to bottom — only those whose target subtype matches are rendered):
///   1.  DEV banner                — universal
///   2.  Construction              — Building, only when IsUnderConstruction
///   3.  Hiring                    — CommercialBuilding
///   4.  Owner                     — CommercialBuilding
///   5.  Jobs (force-hire/-fire)   — CommercialBuilding
///   6.  Storage Roles             — CommercialBuilding (any subtype with SupportedStorageRoles)
///   7.  Safes                     — CommercialBuilding (any subtype with SupportedSafeRoles)
///   8.  Catalog                   — ShopBuilding
///   9.  Cashiers                  — ShopBuilding
///   10. Inventory                 — CommercialBuilding
///
/// Hook: bind change triggers full rebuild; widget callbacks call <see cref="RebuildAll"/>
/// after their action so per-frame Refresh stays cheap (and avoids re-allocating
/// widgets every frame). Per-frame state polling that doesn't change widget shape
/// (e.g. live till balance) is NOT done — devs can re-click to refresh.
/// </summary>
public sealed class BuildingConsoleManagementSubTab : BuildingSubTab
{
    private Building _bound;
    private readonly List<GameObject> _spawnedWidgets = new();

    protected override void DoRefresh(Building b)
    {
        if (b == null) return;
        if (_bound != b)
        {
            _bound = b;
            RebuildAll();
        }
    }

    protected override void DoClear()
    {
        _bound = null;
        ClearWidgets();
    }

    private void ClearWidgets()
    {
        for (int i = 0; i < _spawnedWidgets.Count; i++)
        {
            if (_spawnedWidgets[i] != null) Destroy(_spawnedWidgets[i]);
        }
        _spawnedWidgets.Clear();
    }

    private void RebuildAll()
    {
        ClearWidgets();
        if (_bound == null) return;

        BuildBanner();
        BuildConstructionSection();
        if (_bound is CommercialBuilding cb)
        {
            BuildHiringSection(cb);
            BuildReputationSection(cb);
            BuildOwnerSection(cb);
            BuildJobsSection(cb);
            BuildStorageRolesSection(cb);
            BuildSafesSection(cb);
            if (cb is ShopBuilding sb)
            {
                BuildCatalogSection(sb);
                BuildCashiersSection(sb);
            }
            BuildInventorySection(cb);
        }
    }

    // ─── Sections ────────────────────────────────────────────────────────

    private void BuildBanner()
    {
        var go = new GameObject("DEV_Banner", typeof(RectTransform));
        go.transform.SetParent(transform, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 28; le.preferredHeight = 28;
        var img = go.AddComponent<Image>(); img.color = new Color(0.6f, 0.2f, 0.2f, 1f);

        var labelGO = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(go.transform, worldPositionStays: false);
        var lrt = labelGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8, 0); lrt.offsetMax = new Vector2(-8, 0);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = "[DEV] Console Management — bypasses owner authority";
        tmp.fontSize = 13; tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white; tmp.raycastTarget = false;

        _spawnedWidgets.Add(go);
    }

    private void BuildConstructionSection()
    {
        if (_bound == null || !_bound.IsUnderConstruction) return;
        MakeHeader("Construction");
        var captured = _bound;
        MakeButton("[DEV] Force Finalize", () =>
        {
            if (captured == null || !captured.IsServer) return;
            captured.Finalize();
            // Building flips out of UnderConstruction — rebuild so this section disappears.
            RebuildAll();
        });
    }

    private void BuildHiringSection(CommercialBuilding cb)
    {
        MakeHeader("Hiring");
        var row = MakeRow();
        MakeLabel(cb.IsHiring ? "Currently <color=#64FF64>OPEN</color>" : "Currently <color=#FF6464>CLOSED</color>", row.transform);
        MakeButton(cb.IsHiring ? "[DEV] Close Hiring" : "[DEV] Open Hiring", () =>
        {
            cb.DevForceSetHiring(!cb.IsHiring);
            RebuildAll();
        }, row.transform);
    }

    /// <summary>
    /// Dev-mode reputation panel — current value (colour-coded against the B2B floor)
    /// + four nudge buttons that route through <c>CommercialBuilding.DevForceChangeReputation</c>.
    /// Authored 2026-05-17f. Read-only mirror lives in <see cref="BuildingOverviewSubTab"/>
    /// — this tab adds the mutator buttons.
    /// </summary>
    private void BuildReputationSection(CommercialBuilding cb)
    {
        MakeHeader("Reputation");

        int rep = cb.Reputation;
        string repColor = rep >= CommercialBuilding.ReputationB2BMinimum
            ? "#64FF64"
            : rep > 0 ? "#FFB060" : "#FF6464";
        string suffix = rep < CommercialBuilding.ReputationB2BMinimum
            ? "  <color=grey>(below B2B floor — procurement skips)</color>"
            : "";

        var row = MakeRow();
        MakeLabel($"<color={repColor}>{rep}/{CommercialBuilding.ReputationMax}</color>{suffix}", row.transform);
        MakeButton("[DEV] −10", () => { cb.DevForceChangeReputation(-10); RebuildAll(); }, row.transform);
        MakeButton("[DEV] −1",  () => { cb.DevForceChangeReputation(-1);  RebuildAll(); }, row.transform);
        MakeButton("[DEV] +1",  () => { cb.DevForceChangeReputation(+1);  RebuildAll(); }, row.transform);
        MakeButton("[DEV] +10", () => { cb.DevForceChangeReputation(+10); RebuildAll(); }, row.transform);
    }

    private void BuildOwnerSection(CommercialBuilding cb)
    {
        MakeHeader("Owner");

        // Current owners listing
        bool any = false;
        foreach (var idStr in cb.OwnerIds)
        {
            string id = idStr;
            if (string.IsNullOrEmpty(id)) continue;
            any = true;
            var ch = Character.FindByUUID(id);
            string label = ch != null ? $"{ch.CharacterName} ({id})" : $"<color=#888888>({id})</color> <color=grey>not spawned</color>";
            var row = MakeRow();
            MakeLabel($"• {label}", row.transform);
            if (ch != null)
            {
                var capturedCh = ch;
                MakeButton("[DEV] Remove", () =>
                {
                    if (cb.IsServer) cb.RemoveOwner(capturedCh);
                    RebuildAll();
                }, row.transform);
            }
        }
        if (!any) MakeLabel("<color=grey>(no owners)</color>");

        // Add/transfer/clear by id
        var addRow = MakeRow();
        var inp = MakeInput("characterId", addRow.transform);
        MakeButton("[DEV] Set as Owner (transfer)", () =>
        {
            var ch = Character.FindByUUID(inp.text);
            if (ch == null) { Debug.LogWarning($"<color=magenta>[DevMode]</color> Character '{inp.text}' not found."); return; }
            if (cb.IsServer) cb.SetOwner(ch);
            RebuildAll();
        }, addRow.transform);
        MakeButton("[DEV] Add Owner", () =>
        {
            var ch = Character.FindByUUID(inp.text);
            if (ch == null) { Debug.LogWarning($"<color=magenta>[DevMode]</color> Character '{inp.text}' not found."); return; }
            if (cb.IsServer) cb.AddOwner(ch);
            RebuildAll();
        }, addRow.transform);
        MakeButton("[DEV] Clear All Owners", () =>
        {
            if (cb.IsServer) cb.SetOwner(null); // verified in plan: SetOwner(null) is the canonical clear path
            RebuildAll();
        }, addRow.transform);
    }

    private void BuildJobsSection(CommercialBuilding cb)
    {
        MakeHeader("Jobs");
        var jobs = cb.Jobs;
        if (jobs == null || jobs.Count == 0)
        {
            MakeLabel("<color=grey>(no jobs)</color>");
            return;
        }
        for (int i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            if (job == null) continue;
            var row = MakeRow();
            string title = !string.IsNullOrEmpty(job.JobTitle) ? job.JobTitle : job.GetType().Name;
            string workerName = job.IsAssigned && job.Worker != null ? job.Worker.CharacterName : "<color=grey>(unassigned)</color>";
            MakeLabel($"[{i}] {title} — {workerName}", row.transform);

            var capturedJob = job;
            if (job.IsAssigned && job.Worker != null)
            {
                MakeButton("[DEV] Force-Fire", () =>
                {
                    if (cb.IsServer) cb.RemoveWorker(capturedJob);
                    RebuildAll();
                }, row.transform);
            }
            else
            {
                var inp = MakeInput("characterId", row.transform, minWidth: 100);
                MakeButton("[DEV] Force-Hire", () =>
                {
                    var ch = Character.FindByUUID(inp.text);
                    if (ch == null) { Debug.LogWarning($"<color=magenta>[DevMode]</color> Character '{inp.text}' not found."); return; }
                    if (cb.IsServer) cb.AssignWorker(ch, capturedJob);
                    RebuildAll();
                }, row.transform);
            }
        }
    }

    private void BuildStorageRolesSection(CommercialBuilding cb)
    {
        MakeHeader("Storage Roles");

        var allStorages = cb.GetComponentsInChildren<StorageFurniture>(includeInactive: true);
        if (allStorages == null || allStorages.Length == 0)
        {
            MakeLabel("<color=grey>(no storage furniture under building)</color>");
            return;
        }

        var supported = cb.SupportedStorageRoles;
        if (supported == null || supported.Count == 0)
        {
            MakeLabel("<color=grey>(building reports no SupportedStorageRoles)</color>");
            return;
        }

        for (int i = 0; i < allStorages.Length; i++)
        {
            var s = allStorages[i];
            if (s == null) continue;

            int used = 0;
            for (int sl = 0; sl < s.Capacity; sl++)
            {
                var slot = s.GetItemSlot(sl);
                if (slot != null && !slot.IsEmpty()) used++;
            }

            var row = MakeRow();
            var currentRole = s.Role;
            var currentDescriptor = StorageRoleCatalog.Get(currentRole);
            MakeLabel($"{s.FurnitureName}  <color=#888888>{used}/{s.Capacity} slots · current: {currentDescriptor.Icon} {currentDescriptor.DisplayName}</color>", row.transform);

            // One small button per supported role — clicking switches the storage to that role.
            // Cheaper to author than a programmatic dropdown and matches the dev tool aesthetic.
            for (int r = 0; r < supported.Count; r++)
            {
                var desc = supported[r];
                var capturedStorage = s;
                var capturedRole = desc.Type;
                string label = currentRole == desc.Type
                    ? $"<b>{desc.Icon}</b>"
                    : desc.Icon;
                MakeButton(label, () =>
                {
                    cb.DevForceSetStorageRole(capturedStorage, capturedRole);
                    RebuildAll();
                }, row.transform);
            }
        }
    }

    private void BuildSafesSection(CommercialBuilding cb)
    {
        MakeHeader("Safes");

        var safes = cb.Safes;
        if (safes == null || safes.Count == 0)
        {
            MakeLabel("<color=grey>(no safe furniture under building)</color>");
            return;
        }

        var supported = cb.SupportedSafeRoles;
        if (supported == null || supported.Count == 0)
        {
            MakeLabel("<color=grey>(building reports no SupportedSafeRoles)</color>");
            return;
        }

        // Aggregate-treasury line — quick at-a-glance view of every Treasury safe's
        // contribution. Dev-only sugar; the production management panel surfaces
        // per-safe balances in the same tab via StorageRolesTabSafeRow.
        int aggregateDefault = cb.GetTreasuryBalance(MWI.Economy.CurrencyId.Default);
        MakeLabel($"<color=#FFD27A>Σ Treasury (Default)</color>  <color=#888888>= {aggregateDefault} coin · across {cb.TreasurySafes.Count} treasury-role safe(s)</color>");

        for (int i = 0; i < safes.Count; i++)
        {
            var s = safes[i];
            if (s == null) continue;

            var row = MakeRow();
            var currentRole = s.Role;
            var currentDescriptor = SafeRoleCatalog.Get(currentRole);

            // Per-safe balance summary: roll every currency into one comma-separated
            // string so the row stays one line. Empty balance prints "empty" so the
            // dev can tell zero-balance from missing-data.
            string balanceText;
            var balances = s.Balances;
            if (balances == null || balances.Count == 0)
            {
                balanceText = "empty";
            }
            else
            {
                var sb = new System.Text.StringBuilder(32);
                for (int b = 0; b < balances.Count; b++)
                {
                    if (b > 0) sb.Append(", ");
                    var e = balances[b];
                    sb.Append(e.Amount).Append(' ').Append(e.CurrencyId == 0 ? "Coin" : $"Cur#{e.CurrencyId}");
                }
                balanceText = sb.ToString();
            }

            MakeLabel($"{s.FurnitureName}  <color=#888888>current: {currentDescriptor.Icon} {currentDescriptor.DisplayName} · balance: {balanceText}</color>", row.transform);

            // One small button per supported role — clicking switches the safe to that
            // role via the canonical DoSetSafeRole convergence (same fan-out as the
            // production owner UI + NPC shift-punch).
            for (int r = 0; r < supported.Count; r++)
            {
                var desc = supported[r];
                var capturedSafe = s;
                var capturedRole = desc.Type;
                string label = currentRole == desc.Type
                    ? $"<b>{desc.DisplayName}</b>"
                    : desc.DisplayName;
                MakeButton(label, () =>
                {
                    cb.DevForceSetSafeRole(capturedSafe, capturedRole);
                    RebuildAll();
                }, row.transform);
            }
        }
    }

    private void BuildCatalogSection(ShopBuilding sb)
    {
        MakeHeader("Catalog");
        if (sb.Catalog == null || sb.Catalog.Count == 0)
        {
            MakeLabel("<color=grey>(empty catalog)</color>");
        }
        else
        {
            for (int i = 0; i < sb.Catalog.Count; i++)
            {
                var entry = sb.Catalog[i];
                if (entry.Item == null) continue;
                var row = MakeRow();
                MakeLabel($"{entry.Item.ItemName}  <color=#888888>max={entry.MaxStock}  price={entry.PriceOverride}</color>", row.transform);
                var localId = entry.Item.ItemId;
                MakeButton("[DEV] Remove", () =>
                {
                    sb.DevForceRemoveCatalogEntry(localId);
                    RebuildAll();
                }, row.transform);
            }
        }

        var addRow = MakeRow();
        var idInp = MakeInput("itemId", addRow.transform);
        var maxInp = MakeInput("maxStock", addRow.transform, minWidth: 70);
        var priceInp = MakeInput("price", addRow.transform, minWidth: 70);
        MakeButton("[DEV] Add", () =>
        {
            int.TryParse(maxInp.text, out int max);
            int.TryParse(priceInp.text, out int price);
            sb.DevForceAddCatalogEntry(idInp.text, max, price);
            RebuildAll();
        }, addRow.transform);
    }

    private void BuildCashiersSection(ShopBuilding sb)
    {
        MakeHeader("Cashiers");
        if (sb.Cashiers == null || sb.Cashiers.Count == 0)
        {
            MakeLabel("<color=grey>(no cashiers registered)</color>");
            return;
        }
        for (int i = 0; i < sb.Cashiers.Count; i++)
        {
            var c = sb.Cashiers[i];
            if (c == null) continue;
            var row = MakeRow();
            int balance = c.GetTillBalance(MWI.Economy.CurrencyId.Default);
            string occ = c.Occupant != null ? c.Occupant.CharacterName : "<color=grey>(vacant)</color>";
            MakeLabel($"{c.FurnitureName}  <color=#888888>till={balance}g  vendor={occ}</color>", row.transform);

            var capturedCashier = c;
            MakeButton("[DEV] Withdraw → host", () =>
            {
                var host = ResolveLocalPlayerCharacter();
                if (host == null)
                {
                    Debug.LogWarning("<color=magenta>[DevMode]</color> No local host Character to receive funds.");
                    return;
                }
                sb.DevForceWithdrawCashierTill(capturedCashier, host);
                RebuildAll();
            }, row.transform);
        }
    }

    private void BuildInventorySection(CommercialBuilding cb)
    {
        MakeHeader("Inventory");
        var counts = cb.GetInventoryCountsByItemSO();
        if (counts == null || counts.Count == 0)
        {
            MakeLabel("<color=grey>(empty)</color>");
        }
        else
        {
            var entries = new List<KeyValuePair<ItemSO, int>>(counts);
            entries.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Key == null) continue;
                var row = MakeRow();
                MakeLabel($"{e.Key.ItemName}  <color=#888888>x{e.Value}</color>", row.transform);
                var so = e.Key;
                MakeButton("[DEV] -1", () =>
                {
                    if (!cb.IsServer) return;
                    cb.TakeFromInventory(so); // returns removed instance — dev path discards
                    RebuildAll();
                }, row.transform);
            }
        }

        var addRow = MakeRow();
        var idInp = MakeInput("itemId", addRow.transform);
        MakeButton("[DEV] Add 1", () =>
        {
            if (!cb.IsServer) return;
            var so = ResolveItemSOById(idInp.text);
            if (so == null)
            {
                Debug.LogWarning($"<color=magenta>[DevMode]</color> ItemSO '{idInp.text}' not found in Resources/Data/Item.");
                return;
            }
            // Use the canonical factory — every concrete ItemSO subclass implements
            // CreateInstance(). See WorldItem.cs:107 / 227 for the production callsite.
            ItemInstance inst = so.CreateInstance();
            cb.AddToInventory(inst);
            RebuildAll();
        }, addRow.transform);
    }

    // ─── Widget helpers (programmatic UGUI, dev-only) ────────────────────

    private GameObject MakeRow(Transform parent = null)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
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
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 22; le.flexibleWidth = 1;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = 12;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        tmp.raycastTarget = false; tmp.richText = true;
        _spawnedWidgets.Add(go);
        return go;
    }

    private Button MakeButton(string label, Action onClick, Transform parent = null)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
        var le = go.AddComponent<LayoutElement>(); le.minHeight = 24; le.minWidth = 100;
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

    private TMP_InputField MakeInput(string placeholder, Transform parent = null, float minWidth = 120)
    {
        var go = new GameObject("Input", typeof(RectTransform));
        go.transform.SetParent(parent ?? transform, worldPositionStays: false);
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

        _spawnedWidgets.Add(go);
        return inp;
    }

    private Toggle MakeToggle(string label, bool initial, Action<bool> onChanged, Transform parent = null)
    {
        var row = MakeRow(parent);

        var togGo = new GameObject("Toggle", typeof(RectTransform));
        togGo.transform.SetParent(row.transform, worldPositionStays: false);
        var tle = togGo.AddComponent<LayoutElement>(); tle.minWidth = 22; tle.minHeight = 22;
        var bg = togGo.AddComponent<Image>(); bg.color = new Color(0.30f, 0.30f, 0.30f, 1f);
        var tog = togGo.AddComponent<Toggle>();
        tog.targetGraphic = bg; tog.SetIsOnWithoutNotify(initial);

        var checkGO = new GameObject("Check", typeof(RectTransform));
        checkGO.transform.SetParent(togGo.transform, worldPositionStays: false);
        var crt = checkGO.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.15f, 0.15f); crt.anchorMax = new Vector2(0.85f, 0.85f);
        crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
        var checkImg = checkGO.AddComponent<Image>();
        checkImg.color = new Color(0.7f, 1f, 0.7f, 1f);
        checkImg.raycastTarget = false;
        tog.graphic = checkImg;

        tog.onValueChanged.AddListener(v =>
        {
            try { onChanged?.Invoke(v); }
            catch (Exception e) { Debug.LogException(e); }
        });

        MakeLabel(label, row.transform);
        return tog;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Character ResolveLocalPlayerCharacter()
    {
        try
        {
            if (NetworkManager.Singleton == null) return null;
            var lc = NetworkManager.Singleton.LocalClient;
            if (lc?.PlayerObject == null) return null;
            return lc.PlayerObject.GetComponent<Character>();
        }
        catch (Exception e) { Debug.LogException(e); return null; }
    }

    private static ItemSO ResolveItemSOById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var all = Resources.LoadAll<ItemSO>("Data/Item");
        return Array.Find(all, x => x != null && x.ItemId == id);
    }
}
#endif
