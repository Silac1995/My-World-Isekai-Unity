using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.Interactables;
using MWI.Terrain;
using MWI.WorldSystem;
using MWI.Farming;

/// <summary>
/// Unified resource-node primitive — wild trees, scene-authored ore veins, planted crops,
/// dynamic mineral deposits, etc. Inherits <see cref="InteractableObject"/> to be interactive.
/// Produces items via <see cref="HarvestOutputEntry"/> entries when a character harvests it,
/// optionally despawns or respawns after a delay, optionally allows a destruction (axe / pickaxe)
/// path with separate outputs.
///
/// Three orthogonal configuration axes coexist on this single class:
///
/// 1. **Data root** — either inline serialised fields (`_harvestOutputs`, `_destructionOutputs`,
///    etc.) for hand-authored scene prefabs (`Tree.prefab`, `Gatherable.prefab`), OR a
///    <see cref="HarvestableSO"/> reference (`_so`) that drives every value. When `_so` is a
///    <see cref="CropSO"/>, crop-specific behaviour (maturity gate, perennial refill cycle,
///    growth-stage scaling) automatically engages.
///
/// 2. **Cell coupling** — when <see cref="InitializeAtStage"/> is called with valid CellX/CellZ
///    + Grid + Map, the harvestable participates in <see cref="FarmGrowthSystem"/>'s daily
///    tick (growth, perennial refill, etc.). Free-positioned nodes (cellX = -1) skip all
///    cell-coupled logic — they're static scene content.
///
/// 3. **Networking** — when a sibling <see cref="HarvestableNetSync"/> NetworkBehaviour is
///    present, three NetworkVariables (CurrentStage, IsDepleted, CropIdNet) drive client-
///    visible state. Without NetSync (e.g. wild scene-placed trees), the harvestable is
///    server-only state — clients see a static visual and harvest mutations don't replicate.
///
/// History: 2026-04-29 unification folded the previous `CropHarvestable` subclass into this
/// class — there is now one component for every resource node in the project.
/// </summary>
public class Harvestable : InteractableObject
{
    [Header("Data root (optional — overrides inline fields when set)")]
    [Tooltip("HarvestableSO drives every runtime value (outputs, tools, depletion, sprites, prefab). When set, the inline serialised fields below act as fallbacks. CropSO subclasses additionally enable maturity gating + perennial refill.")]
    [SerializeField] private HarvestableSO _so;

    [Header("Harvestable")]
    [SerializeField] private HarvestableCategory _category = HarvestableCategory.Wood;
    [SerializeField] private List<HarvestOutputEntry> _harvestOutputs = new List<HarvestOutputEntry>();
    [SerializeField] private float _harvestDuration = 3f;
    [SerializeField] private bool _isDepletable = true;
    [SerializeField] private int _maxHarvestCount = 5;
    [SerializeField, Tooltip("Number of in-game days before the resource respawns (non-cell-coupled only — cell-coupled crops are managed by FarmGrowthSystem)")]
    private int _respawnDelayDays = 1;

    [Header("Yield (the default 'pick' interaction)")]
    [Tooltip("If null, bare hands (or any held item) work for the yield path.")]
    [SerializeField] private ItemSO _requiredHarvestTool;

    [Header("Destruction (axe / pickaxe etc.)")]
    [SerializeField] private bool _allowDestruction;
    [Tooltip("When true (default), NPCs (HarvestingBuilding workers) may autonomously destroy this harvestable to obtain its destruction outputs. Set to false to protect a node from autonomous NPC consumption (the player's Hold-E → Destroy menu still works regardless). AllowDestruction must also be true for either path to fire.")]
    [SerializeField] private bool _allowNpcDestruction = true;
    [SerializeField] private ItemSO _requiredDestructionTool;
    [SerializeField] private List<HarvestOutputEntry> _destructionOutputs = new List<HarvestOutputEntry>();
    [SerializeField] private float _destructionDuration = 3f;

    [Header("Visuals (optional)")]
    [Tooltip("Toggled inactive on Deplete and active on Respawn for the inline-field path. Crop-style visual swap (ready ↔ depleted sprite) uses _spriteRenderer + _readySprite + _depletedSprite below.")]
    [SerializeField] private GameObject _visualRoot;
    [SerializeField] private SpriteRenderer _spriteRenderer;
    [SerializeField] private Sprite _readySprite;
    [SerializeField] private Sprite _depletedSprite;

    private int _currentHarvestCount = 0;
    private bool _isDepleted = false;
    private int _targetRespawnDay = 0;

    // ── Cell coupling (optional, server-only). Set by InitializeAtStage when a runtime
    //    spawner (FarmGrowthSystem, future BuildingPlacementManager-for-resources, etc.)
    //    anchors this harvestable to a specific TerrainCellGrid cell. Sentinel values (-1)
    //    mean "not cell-coupled" — the harvestable is a static scene node like Tree.prefab.
    public int CellX { get; protected set; } = -1;
    public int CellZ { get; protected set; } = -1;
    public TerrainCellGrid Grid { get; protected set; }
    protected MapController _map;

    /// <summary>True when this harvestable is bound to a TerrainCellGrid cell. Drives the
    /// cell-mutation branches in OnDepleted / OnDestroyed and disables the base auto-respawn
    /// (cell-coupled refill is owned by FarmGrowthSystem).</summary>
    public bool IsCellCoupled => CellX >= 0 && CellZ >= 0 && Grid != null;

    // ── Runtime caches (populated by Awake + InitializeAtStage). All optional.
    private HarvestableNetSync _netSync;
    private FarmGrowthSystem _farmGrowthSystem;
    private CropSO _crop;          // Cached cast of _so to CropSO (or registry-resolved on clients)
    private Vector3 _baseScale;    // Captured at Awake before any runtime scaling

    // Last NetVar values seen by the polling fallback in Update. Tracks change so we only
    // re-run ApplyVisual when something actually flipped, not every frame. Defensive backup
    // for NGO OnValueChanged callbacks that have shown intermittent firing on remote clients.
    private int _lastSeenStage = -1;
    private bool _lastSeenDepleted;
    private string _lastSeenCropId = "";

    public event System.Action<Harvestable> OnRespawned;

    /// <summary>
    /// Fires whenever this harvestable's harvest-availability state flips: depleted on harvest,
    /// refilled / respawned, or transitioned via the perennial cycle (SetReady / SetDepleted).
    /// Subscribers (e.g. <see cref="HarvestingBuilding"/>) decide what to do based on
    /// <see cref="CanHarvest"/> at the time the event fires — registering / cancelling
    /// HarvestResourceTask. Unifies the legacy <see cref="OnRespawned"/> (auto-respawn-after-N-days
    /// for wild scenery) and the perennial refill cycle (FarmGrowthSystem flipping IsDepleted)
    /// under one subscription so building consumers don't need to special-case the path.
    /// </summary>
    public event System.Action<Harvestable> OnStateChanged;

    /// <summary>Fires <see cref="OnStateChanged"/> safely (null-checked).</summary>
    protected void RaiseStateChanged() => OnStateChanged?.Invoke(this);

    // ── Awake / Update ──────────────────────────────────────────────────────────

    protected virtual void Awake()
    {
        _netSync = GetComponent<HarvestableNetSync>();
        _baseScale = transform.localScale;
        if (_so is CropSO c) _crop = c;
    }

    /// <summary>
    /// Polls the three replicated NetVars every frame and triggers ApplyVisual on any
    /// change. Defensive fallback to the OnValueChanged subscriptions in HarvestableNetSync —
    /// those callbacks reliably fire on the host (server-side) but have shown intermittent
    /// behaviour on remote clients (initial-sync replicates correctly, post-spawn
    /// CurrentStage / IsDepleted updates sometimes don't trigger the registered callback).
    /// Cheap (3 NetVar reads + 3 compares + 1 string compare per frame per active harvestable),
    /// idempotent (no-op on no-change). Skipped for harvestables without a NetSync sibling.
    /// </summary>
    protected virtual void Update()
    {
        if (_netSync == null) return;

        int currStage = _netSync.CurrentStage.Value;
        bool currDepleted = _netSync.IsDepleted.Value;
        string currCropId = _netSync.CropIdNet.Value.ToString();

        if (currStage == _lastSeenStage && currDepleted == _lastSeenDepleted && currCropId == _lastSeenCropId)
            return;

        _lastSeenStage = currStage;
        _lastSeenDepleted = currDepleted;
        _lastSeenCropId = currCropId;
        ApplyVisual();
    }

    private void OnDestroy()
    {
        if (_isDepleted && MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }

    // ── Public read-only state ──────────────────────────────────────────────────

    public IReadOnlyList<HarvestOutputEntry> HarvestOutputs => _harvestOutputs;
    public HarvestableCategory Category => _category;
    public float HarvestDuration => _harvestDuration;
    public bool IsDepleted => _isDepleted;
    public int RemainingYield => _isDepletable ? Mathf.Max(0, _maxHarvestCount - _currentHarvestCount) : int.MaxValue;
    public ItemSO RequiredHarvestTool => _requiredHarvestTool;
    public IReadOnlyList<HarvestOutputEntry> DestructionOutputs => _destructionOutputs;
    public float DestructionDuration => _destructionDuration;
    public HarvestableSO SO => _so;

    /// <summary>True if this harvestable opts in to destruction. Reads via the resolved CropSO
    /// when the harvestable is crop-aware (so non-host clients see the correct value); falls
    /// back to the inline serialised field for wild scenery.</summary>
    public virtual bool AllowDestruction
    {
        get
        {
            var crop = ResolveCropFromNet();
            if (crop != null) return crop.AllowDestruction;
            if (_so != null) return _so.AllowDestruction;
            return _allowDestruction;
        }
    }

    /// <summary>True if NPCs may autonomously destroy this harvestable (in addition to
    /// <see cref="AllowDestruction"/>). Default is TRUE — designers opt OUT per resource node
    /// they want to protect from NPC consumption (e.g. landmark trees, decorative ore veins
    /// that should never be mined autonomously). Players are not gated by this flag; they
    /// can still hold E → Destroy on any harvestable that has AllowDestruction enabled.</summary>
    public virtual bool AllowNpcDestruction
    {
        get
        {
            if (_so != null) return _so.AllowNpcDestruction;
            return _allowNpcDestruction;
        }
    }

    /// <summary>Tool required for the destruction path; null = any tool. Same dual-source
    /// rule as <see cref="AllowDestruction"/>.</summary>
    public virtual ItemSO RequiredDestructionTool
    {
        get
        {
            var crop = ResolveCropFromNet();
            if (crop != null) return crop.RequiredDestructionTool as ItemSO;
            return _requiredDestructionTool;
        }
    }

    // ── Predicates ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Server- AND client-readable. For crop-aware harvestables (a CropSO is attached and a
    /// HarvestableNetSync sibling exists), reads only network-replicated state — non-host
    /// clients can decide whether to fire a harvest action. Falls back to inline fields for
    /// wild scenery harvestables that have neither.
    /// </summary>
    public virtual bool CanHarvest()
    {
        var crop = ResolveCropFromNet();
        if (crop != null && _netSync != null)
        {
            // Crop-aware path: must be mature + not depleted (read from NetVar state).
            if (_netSync.CurrentStage.Value < crop.DaysToMature) return false;
            if (_netSync.IsDepleted.Value) return false;
            return crop.HarvestOutputs != null && crop.HarvestOutputs.Count > 0;
        }
        // Default path: inline-field check.
        return !_isDepleted && HasAnyEntryWithItem(_harvestOutputs);
    }

    private static bool HasAnyEntryWithItem(List<HarvestOutputEntry> entries)
    {
        if (entries == null) return false;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Item != null && entries[i].Count > 0) return true;
        return false;
    }

    public bool CanHarvestWith(ItemSO heldItem)
    {
        if (!CanHarvest()) return false;
        return _requiredHarvestTool == null || heldItem == _requiredHarvestTool;
    }

    public bool CanDestroyWith(ItemSO heldItem)
    {
        if (!AllowDestruction) return false;
        var tool = RequiredDestructionTool;
        return tool == null || heldItem == tool;
    }

    /// <summary>True if the YIELD path produces this item (i.e. picking the harvestable yields it).
    /// Existing callers that don't care about destruction outputs keep using this.</summary>
    public bool HasOutput(ItemSO item) => HasYieldOutput(item);

    /// <summary>True if any item in the list is produced by the YIELD path.</summary>
    public bool HasAnyOutput(List<ItemSO> items) => HasAnyYieldOutput(items);

    // ── Yield / destruction / union accessors (post-2026-04-29 split) ────────────
    // Three return-methods + matching predicates so callers can ask, precisely,
    // "which items does this harvestable produce when picked?", "…when destroyed?",
    // or "…produced anywhere?" The HarvestingBuilding scan uses the union form to
    // discover both crops AND destruction-only nodes (e.g. a dead tree that drops
    // wood on chop). The yield-only variants stay the default for callers that
    // shouldn't claim a destruction-only target as a HarvestResourceTask.

    /// <summary>Distinct ItemSOs produced when this harvestable is picked (yield path).</summary>
    public IEnumerable<ItemSO> GetYieldItems()
    {
        if (_harvestOutputs == null) yield break;
        for (int i = 0; i < _harvestOutputs.Count; i++)
        {
            var it = _harvestOutputs[i].Item;
            if (it != null && _harvestOutputs[i].Count > 0) yield return it;
        }
    }

    /// <summary>Distinct ItemSOs produced when this harvestable is destroyed (axe/pickaxe path).</summary>
    public IEnumerable<ItemSO> GetDestructionItems()
    {
        if (_destructionOutputs == null) yield break;
        for (int i = 0; i < _destructionOutputs.Count; i++)
        {
            var it = _destructionOutputs[i].Item;
            if (it != null && _destructionOutputs[i].Count > 0) yield return it;
        }
    }

    /// <summary>Union — every ItemSO this harvestable can produce via either yield or destruction.
    /// Suitable for "does this harvestable supply my building's wanted items via ANY mechanism?"
    /// queries during HarvestingBuilding zone scans.</summary>
    public IEnumerable<ItemSO> GetAllProducibleItems()
    {
        var seen = new HashSet<ItemSO>();
        foreach (var it in GetYieldItems()) if (seen.Add(it)) yield return it;
        foreach (var it in GetDestructionItems()) if (seen.Add(it)) yield return it;
    }

    /// <summary>True if the YIELD path produces this item.</summary>
    public bool HasYieldOutput(ItemSO item)
    {
        if (item == null || _harvestOutputs == null) return false;
        for (int i = 0; i < _harvestOutputs.Count; i++)
            if (_harvestOutputs[i].Item == item) return true;
        return false;
    }

    /// <summary>True if the DESTRUCTION path produces this item.</summary>
    public bool HasDestructionOutput(ItemSO item)
    {
        if (item == null || _destructionOutputs == null) return false;
        for (int i = 0; i < _destructionOutputs.Count; i++)
            if (_destructionOutputs[i].Item == item) return true;
        return false;
    }

    /// <summary>True if any item in the list is produced via the YIELD path.</summary>
    public bool HasAnyYieldOutput(IList<ItemSO> items)
    {
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
            if (HasYieldOutput(items[i])) return true;
        return false;
    }

    /// <summary>True if any item in the list is produced via the DESTRUCTION path.</summary>
    public bool HasAnyDestructionOutput(IList<ItemSO> items)
    {
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
            if (HasDestructionOutput(items[i])) return true;
        return false;
    }

    /// <summary>True if any item in the list is produced via EITHER yield OR destruction (union).</summary>
    public bool HasAnyProducibleOutput(IList<ItemSO> items)
    {
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
            if (HasYieldOutput(items[i]) || HasDestructionOutput(items[i])) return true;
        return false;
    }

    // ── Interaction ──────────────────────────────────────────────────────────────

    public override void Interact(Character interactor)
    {
        if (interactor == null || interactor.CharacterActions == null) return;
        var held = GetHeldItemSO(interactor);

        if (CanHarvestWith(held))
        {
            var gatherAction = new CharacterHarvestAction(interactor, this);
            interactor.CharacterActions.ExecuteAction(gatherAction);
        }
    }

    /// <summary>
    /// Returns the rows shown by the Hold-E menu. For crop-aware harvestables, reads from the
    /// resolved CropSO so non-host clients can render the correct rows even though their
    /// inline `_harvestOutputs` field is empty. For wild scenery, uses the inline fields.
    /// </summary>
    public virtual IList<HarvestInteractionOption> GetInteractionOptions(Character actor)
    {
        var list = new List<HarvestInteractionOption>(2);
        var held = GetHeldItemSO(actor);
        var crop = ResolveCropFromNet();

        if (crop != null)
        {
            // ── Crop-aware path: SO drives the menu (works on every peer). ──
            ItemSO firstYield = FirstSOEntry(crop.HarvestOutputs);
            if (firstYield != null)
            {
                bool yieldOk = CanHarvestWith(held);
                string yieldReason = null;
                if (!yieldOk)
                {
                    if (_netSync != null && _netSync.CurrentStage.Value < crop.DaysToMature) yieldReason = "Not yet ripe";
                    else if (_netSync != null && _netSync.IsDepleted.Value) yieldReason = "Already harvested";
                    else if (RequiredHarvestTool != null) yieldReason = $"Requires {RequiredHarvestTool.ItemName}";
                    else yieldReason = "Cannot harvest";
                }
                list.Add(new HarvestInteractionOption(
                    label: $"Pick {firstYield.ItemName}",
                    icon: firstYield.Icon,
                    outputPreview: BuildSOPreview(crop.HarvestOutputs),
                    isAvailable: yieldOk,
                    unavailableReason: yieldReason,
                    actionFactory: ch => new CharacterHarvestAction(ch, this)));
            }

            if (crop.AllowDestruction)
            {
                ItemSO firstDestroy = FirstSOEntry(crop.DestructionOutputs);
                if (firstDestroy != null)
                {
                    var requiredTool = crop.RequiredDestructionTool as ItemSO;
                    bool destroyOk = requiredTool == null || held == requiredTool;
                    string destroyReason = destroyOk ? null
                        : (requiredTool != null ? $"Requires {requiredTool.ItemName}" : "Cannot destroy");
                    list.Add(new HarvestInteractionOption(
                        label: "Destroy",
                        icon: firstDestroy.Icon,
                        outputPreview: BuildSOPreview(crop.DestructionOutputs),
                        isAvailable: destroyOk,
                        unavailableReason: destroyReason,
                        actionFactory: ch => new CharacterAction_DestroyHarvestable(ch, this)));
                }
            }
            return list;
        }

        // ── Default path: inline-field-driven menu (wild scenery harvestables). ──
        var firstHarvest = FirstNonEmptyEntry(_harvestOutputs);
        if (firstHarvest.Item != null)
        {
            bool yieldOk = CanHarvestWith(held);
            string yieldReason = null;
            if (!yieldOk)
            {
                if (_isDepleted) yieldReason = "Already harvested";
                else if (_requiredHarvestTool != null) yieldReason = $"Requires {_requiredHarvestTool.ItemName}";
                else yieldReason = "Cannot harvest";
            }
            list.Add(new HarvestInteractionOption(
                label: $"Pick {firstHarvest.Item.ItemName}",
                icon: firstHarvest.Item.Icon,
                outputPreview: BuildOutputPreview(_harvestOutputs),
                isAvailable: yieldOk,
                unavailableReason: yieldReason,
                actionFactory: ch => new CharacterHarvestAction(ch, this)));
        }

        var firstDestruction = FirstNonEmptyEntry(_destructionOutputs);
        if (_allowDestruction && firstDestruction.Item != null)
        {
            bool destroyOk = CanDestroyWith(held);
            string destroyReason = destroyOk ? null
                : (_requiredDestructionTool != null ? $"Requires {_requiredDestructionTool.ItemName}" : "Cannot destroy");
            list.Add(new HarvestInteractionOption(
                label: "Destroy",
                icon: firstDestruction.Item.Icon,
                outputPreview: BuildOutputPreview(_destructionOutputs),
                isAvailable: destroyOk,
                unavailableReason: destroyReason,
                actionFactory: ch => new CharacterAction_DestroyHarvestable(ch, this)));
        }

        return list;
    }

    private static HarvestOutputEntry FirstNonEmptyEntry(List<HarvestOutputEntry> entries)
    {
        if (entries == null) return default;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Item != null && entries[i].Count > 0) return entries[i];
        return default;
    }

    private static ItemSO FirstSOEntry(IReadOnlyList<HarvestableOutputEntry> entries)
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Item is ItemSO it && it != null && entries[i].Count > 0) return it;
        return null;
    }

    private static string BuildOutputPreview(List<HarvestOutputEntry> entries)
    {
        if (entries == null || entries.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        bool first = true;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Item == null || e.Count <= 0) continue;
            if (!first) sb.Append(", ");
            sb.Append(e.Count).Append("× ").Append(e.Item.ItemName);
            first = false;
        }
        return sb.ToString();
    }

    private static string BuildSOPreview(IReadOnlyList<HarvestableOutputEntry> entries)
    {
        if (entries == null || entries.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        bool first = true;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!(e.Item is ItemSO it) || it == null || e.Count <= 0) continue;
            if (!first) sb.Append(", ");
            sb.Append(e.Count).Append("× ").Append(it.ItemName);
            first = false;
        }
        return sb.ToString();
    }

    // ── Server-only mutation ─────────────────────────────────────────────────────

    /// <summary>
    /// Server-only. Commits one harvest pass. Increments the harvest counter, calls Deplete
    /// when it hits the max, and returns the entries the caller drops as WorldItems
    /// (each entry's Item × Count). Returns null on failure (depleted, no outputs, null harvester).
    /// </summary>
    public IReadOnlyList<HarvestOutputEntry> Harvest(Character harvester)
    {
        if (harvester == null || !CanHarvest()) return null;
        if (_harvestOutputs == null || _harvestOutputs.Count == 0) return null;

        _currentHarvestCount++;

        NotifyHarvesterQuestProgress(harvester);

        if (_isDepletable && _currentHarvestCount >= _maxHarvestCount)
            Deplete();

        Debug.Log($"<color=green>[Harvest]</color> {harvester.CharacterName} harvested from {gameObject.name} ({_harvestOutputs.Count} entry types). _isDepleted={_isDepleted}.");
        return _harvestOutputs;
    }

    /// <summary>Server-only. Spawns destruction outputs as WorldItems and despawns this
    /// harvestable. Optionally records quest progress on the destroyer's
    /// <see cref="DestroyHarvestableTask"/>, mirroring how <see cref="Harvest"/> records
    /// progress on a matching <see cref="HarvestResourceTask"/>. Pass null for non-character
    /// callers (e.g. environmental decay).
    ///
    /// Returns the list of spawned <see cref="WorldItem"/> instances so the caller (typically
    /// <see cref="CharacterActions.ApplyDestroyOnServer"/>) can register a
    /// <see cref="PickupLooseItemTask"/> on the destroyer's workplace for each — without that
    /// follow-up, the harvester's planner has no <c>looseItemExists</c> trigger and the
    /// dropped wood/etc. just sits on the ground. Mirrors <c>ApplyHarvestOnServer</c>'s
    /// task-registration pass on the harvest path.</summary>
    public List<WorldItem> DestroyForOutputs(Character destroyer = null)
    {
        var spawned = new List<WorldItem>();
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return spawned;

        // Record quest progress BEFORE the GameObject despawns so the player's HUD reflects
        // the completion before the IsValid auto-completion path fires (target becomes null).
        NotifyDestroyerQuestProgress(destroyer);

        for (int i = 0; i < _destructionOutputs.Count; i++)
        {
            var entry = _destructionOutputs[i];
            if (entry.Item == null || entry.Count <= 0) continue;
            for (int n = 0; n < entry.Count; n++)
            {
                var item = SpawnDestructionItem(entry.Item);
                if (item != null) spawned.Add(item);
            }
        }

        OnDestroyed();

        if (TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
            netObj.Despawn();
        else
            Destroy(gameObject);

        return spawned;
    }

    /// <summary>Server-only. Called by FarmGrowthSystem on each "Grew" outcome for crop-aware
    /// cell-coupled harvestables.</summary>
    public void AdvanceStage()
    {
        if (_crop == null || _netSync == null) return;
        if (_netSync.CurrentStage.Value < _crop.DaysToMature)
            _netSync.CurrentStage.Value = _netSync.CurrentStage.Value + 1;
    }

    /// <summary>Server-only. Called by FarmGrowthSystem after RegrowDays for perennials.</summary>
    public void Refill() => SetReady();

    public void SetReady()
    {
        ResetHarvestState();
        if (_netSync != null) _netSync.IsDepleted.Value = false;
        RaiseStateChanged();
    }

    public void SetDepleted()
    {
        MarkDepletedNoCallback();
        if (_netSync != null) _netSync.IsDepleted.Value = true;
        RaiseStateChanged();
    }

    // ── Lifecycle hooks ──────────────────────────────────────────────────────────

    protected virtual void OnDestroyed()
    {
        if (!IsCellCoupled) return;
        ref var cell = ref Grid.GetCellRef(CellX, CellZ);
        ClearCellAndUnregister(ref cell);
    }

    /// <summary>
    /// Called on Deplete. For cell-coupled crop-aware harvestables, branches on perennial
    /// vs one-shot: perennials reset their cell's TimeSinceLastWatered to 0 (entering refill
    /// mode) and flip the IsDepleted NetVar; one-shots clear the cell entirely and despawn
    /// the NetworkObject. For non-cell-coupled scenery, default no-op (the base auto-respawn
    /// path runs through HandleNewDay → Respawn).
    /// </summary>
    protected virtual void OnDepleted()
    {
        if (!IsCellCoupled || _crop == null) return;

        ref var cell = ref Grid.GetCellRef(CellX, CellZ);
        if (_crop.IsPerennial)
        {
            cell.TimeSinceLastWatered = 0f;
            if (_netSync != null) _netSync.IsDepleted.Value = true;
        }
        else
        {
            ClearCellAndUnregister(ref cell);
            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned) netObj.Despawn();
        }
    }

    private void ClearCellAndUnregister(ref TerrainCell cell)
    {
        cell.PlantedCropId = null;
        cell.GrowthTimer = 0f;
        cell.TimeSinceLastWatered = -1f;
        if (_farmGrowthSystem != null) _farmGrowthSystem.UnregisterHarvestable(CellX, CellZ);
    }

    protected void ResetHarvestState()
    {
        _currentHarvestCount = 0;
        _isDepleted = false;
        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        if (_visualRoot != null)
            _visualRoot.SetActive(true);
    }

    protected void MarkDepletedNoCallback()
    {
        _isDepleted = true;
        _currentHarvestCount = _maxHarvestCount;
    }

    private void NotifyHarvesterQuestProgress(Character harvester)
    {
        if (harvester == null || harvester.CharacterQuestLog == null) return;
        foreach (var quest in harvester.CharacterQuestLog.ActiveQuests)
        {
            if (quest is HarvestResourceTask hrt && hrt.HarvestableTarget == this)
            {
                quest.RecordProgress(harvester, 1);
                return;
            }
        }
    }

    /// <summary>Quest-log progress hook for the destruction path. Mirrors
    /// <see cref="NotifyHarvesterQuestProgress"/> but matches against the destroy task
    /// type so the player's HUD ticks "1/1 destroyed" before <c>IsValid</c> auto-completes
    /// the task (target gets despawned by <c>CharacterAction_DestroyHarvestable</c>).
    /// Called from <see cref="DestroyForOutputs"/> right before the destruction items are
    /// spawned + the GameObject despawns.</summary>
    private void NotifyDestroyerQuestProgress(Character destroyer)
    {
        if (destroyer == null || destroyer.CharacterQuestLog == null) return;
        foreach (var quest in destroyer.CharacterQuestLog.ActiveQuests)
        {
            if (quest is DestroyHarvestableTask dht && dht.HarvestableTarget == this)
            {
                quest.RecordProgress(destroyer, 1);
                return;
            }
        }
    }

    internal static ItemSO GetHeldItemSO(Character actor)
    {
        if (actor == null) return null;
        var hands = actor.CharacterVisual != null && actor.CharacterVisual.BodyPartsController != null
            ? actor.CharacterVisual.BodyPartsController.HandsController
            : null;
        return hands != null && hands.CarriedItem != null ? hands.CarriedItem.ItemSO : null;
    }

    private WorldItem SpawnDestructionItem(ItemSO item)
    {
        if (item == null) return null;
        var pos = transform.position + Random.insideUnitSphere * 0.5f;
        pos.y = transform.position.y;
        return WorldItem.SpawnWorldItem(item, pos);
    }

    /// <summary>Depletes the resource. Drives the visual swap path (visualRoot disable for
    /// inline-field harvestables; sprite swap via ApplyVisual for crop-aware ones), schedules
    /// auto-respawn for non-cell-coupled, and fires the OnDepleted hook.</summary>
    protected virtual void Deplete()
    {
        _isDepleted = true;

        ScheduleRespawnAfterDeplete();

        if (_visualRoot != null)
            _visualRoot.SetActive(false);

        Debug.Log($"<color=orange>[Harvest]</color> {gameObject.name} is depleted. Respawn scheduled for day {_targetRespawnDay}.");

        OnDepleted();
        // After OnDepleted has updated cell state / NetVars, refresh the visual so the sprite
        // swap (crop-aware path) is visible to the server immediately.
        ApplyVisual();
        RaiseStateChanged();
    }

    /// <summary>Subscribes to OnNewDay so the resource auto-respawns after _respawnDelayDays.
    /// Skipped for cell-coupled harvestables — their refill is owned by FarmGrowthSystem
    /// (perennial PHASE C) or they're one-shot (despawned in OnDepleted).</summary>
    protected virtual void ScheduleRespawnAfterDeplete()
    {
        if (IsCellCoupled) return;
        if (MWI.Time.TimeManager.Instance != null)
        {
            _targetRespawnDay = MWI.Time.TimeManager.Instance.CurrentDay + _respawnDelayDays;
            MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        }
    }

    private void HandleNewDay()
    {
        if (MWI.Time.TimeManager.Instance != null && MWI.Time.TimeManager.Instance.CurrentDay >= _targetRespawnDay)
            Respawn();
    }

    private void Respawn()
    {
        _isDepleted = false;
        _currentHarvestCount = 0;

        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;

        if (_visualRoot != null)
            _visualRoot.SetActive(true);

        Debug.Log($"<color=green>[Harvest]</color> {gameObject.name} has respawned!");
        OnRespawned?.Invoke(this);
        RaiseStateChanged();
    }

    // ── Initialise-at-stage entry point ─────────────────────────────────────────

    /// <summary>
    /// Server-only unified entry point for runtime-spawned harvestables. Configures the
    /// instance from a <see cref="HarvestableSO"/> + an optional cell anchor + an explicit
    /// starting state. Replaces the legacy `CropHarvestable.InitializeFromCell` flow with a
    /// shape that works for any resource node (crops, ore veins, dynamic minerals, …).
    /// Pass <paramref name="map"/> + <paramref name="cellX"/>/<paramref name="cellZ"/> to
    /// bind to a cell; pass cellX = -1 for free-positioned nodes.
    ///
    /// <para><b>Stage semantics</b>: <paramref name="startStage"/> drives "is this mature
    /// yet" gating for crop-aware harvestables. Pass 0 for fresh-planted, the SO's mature
    /// value for an instantly-mature spawn (quest-given apple tree, designer-placed ore
    /// vein), or any value in-between.</para>
    ///
    /// <para><b>Depleted semantics</b>: <paramref name="startDepleted"/> = true marks the
    /// instance as already harvested. Used by post-load reconstruction of perennials whose
    /// cell encodes "in refill cycle" (TimeSinceLastWatered ≥ 0).</para>
    /// </summary>
    public virtual void InitializeAtStage(HarvestableSO so, int startStage = 0, bool startDepleted = false,
                                          MapController map = null, int cellX = -1, int cellZ = -1,
                                          TerrainCellGrid grid = null)
    {
        if (so != null)
        {
            _so = so;
            CopySOToInlineFields(so);
            if (so is CropSO c) _crop = c;
        }

        _map = map;
        Grid = grid;
        CellX = cellX;
        CellZ = cellZ;
        if (IsCellCoupled && _map != null)
            _farmGrowthSystem = _map.GetComponent<FarmGrowthSystem>();

        if (_netSync == null) _netSync = GetComponent<HarvestableNetSync>();

        if (startDepleted)
            MarkDepletedNoCallback();
        else
            ResetHarvestState();

        // Push the staged values onto the NetSync sibling (if present) so clients see the
        // crop's current state immediately. CropIdNet drives the client-side ResolveCropFromNet
        // → menu population path; CurrentStage gates maturity; IsDepleted drives the perennial
        // ready ↔ depleted sprite swap.
        if (_netSync != null && _crop != null)
        {
            _netSync.CropIdNet.Value = new FixedString64Bytes(_crop.Id ?? string.Empty);
            _netSync.CurrentStage.Value = Mathf.Clamp(startStage, 0, _crop.DaysToMature);
            _netSync.IsDepleted.Value = startDepleted;
        }

        ApplyVisual();
    }

    /// <summary>Copy SO-driven runtime values into the inline serialised mirror so the
    /// existing inline-field-driven paths keep working without per-call SO lookups. CropSO
    /// extensions (PlantDuration, RegrowDays, …) are not mirrored — they're read directly
    /// from `_so` / `_crop` on the crop-aware paths.</summary>
    protected virtual void CopySOToInlineFields(HarvestableSO so)
    {
        if (so == null) return;

        _harvestDuration = so.HarvestDuration;
        _isDepletable = so.IsDepletable;
        _maxHarvestCount = so.IsDepletable ? so.MaxHarvestCount : 0;
        _respawnDelayDays = so.RespawnDelayDays;

        _requiredHarvestTool = so.RequiredHarvestTool as ItemSO;
        _harvestOutputs = CastEntryList(so.HarvestOutputs);

        _allowDestruction = so.AllowDestruction;
        _allowNpcDestruction = so.AllowNpcDestruction;
        _requiredDestructionTool = so.RequiredDestructionTool as ItemSO;
        _destructionOutputs = CastEntryList(so.DestructionOutputs);
        _destructionDuration = so.DestructionDuration;

        if (so.ReadySprite != null) _readySprite = so.ReadySprite;
        if (so.DepletedSprite != null) _depletedSprite = so.DepletedSprite;

        // CropSO-specific runtime overrides for cell-coupled crops:
        // 1 yield per harvest, depletable, no auto-respawn (FarmGrowthSystem owns the cycle).
        if (so is CropSO)
        {
            _maxHarvestCount = 1;
            _isDepletable = true;
            _respawnDelayDays = 0;
        }
    }

    private static List<HarvestOutputEntry> CastEntryList(IReadOnlyList<HarvestableOutputEntry> source)
    {
        var dst = new List<HarvestOutputEntry>(source != null ? source.Count : 0);
        if (source == null) return dst;
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i].Item is ItemSO item && item != null && source[i].Count > 0)
                dst.Add(new HarvestOutputEntry(item, source[i].Count));
        }
        return dst;
    }

    // ── NetSync callback bridges ─────────────────────────────────────────────────

    /// <summary>Invoked by <see cref="HarvestableNetSync"/> on every NetVar change and once
    /// on OnNetworkSpawn. Drives the visual refresh.</summary>
    public virtual void OnNetSyncChanged() => ApplyVisual();

    /// <summary>Invoked by <see cref="HarvestableNetSync"/> when the CropIdNet NetVar arrives
    /// on a client. Resolves the CropSO from the registry and triggers ApplyVisual.</summary>
    public virtual void OnCropIdResolved()
    {
        if (_crop != null) return;
        if (_netSync == null) return;
        string id = _netSync.CropIdNet.Value.ToString();
        if (string.IsNullOrEmpty(id)) return;
        _crop = CropRegistry.Get(id);
        if (_crop != null && _so == null) _so = _crop;   // hydrate _so from the registry on clients
        ApplyVisual();
    }

    /// <summary>
    /// Resolves the CropSO from either the inline `_so` reference (server-side, set by
    /// InitializeAtStage) or the replicated CropIdNet NetVar (client-side, set by NGO sync).
    /// Returns null for non-crop-aware harvestables (wild trees, plain Harvestable). Caches
    /// into `_crop` so subsequent calls are O(1).
    /// </summary>
    private CropSO ResolveCropFromNet()
    {
        if (_crop != null) return _crop;
        if (_so is CropSO cs) { _crop = cs; return _crop; }
        if (_netSync == null) return null;
        string id = _netSync.CropIdNet.Value.ToString();
        if (string.IsNullOrEmpty(id)) return null;
        _crop = CropRegistry.Get(id);
        return _crop;
    }

    // ── Visual ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the current networked state to the visual. For crop-aware harvestables, lerps
    /// transform.localScale from 0.25 (stage 0) → 1.0 (mature) and swaps the sprite between
    /// per-stage / ready / depleted variants. For non-crop-aware harvestables, no-op (the
    /// inline-field path uses _visualRoot SetActive instead).
    /// </summary>
    private void ApplyVisual()
    {
        if (_netSync == null) return;
        var crop = ResolveCropFromNet();
        if (crop == null) return;

        int stage = _netSync.CurrentStage.Value;
        bool mature = stage >= crop.DaysToMature;

        // Scale: tiny when fresh-planted, full size at maturity. Cached _baseScale survives
        // prefab variant scale overrides.
        float t = crop.DaysToMature > 0 ? Mathf.Clamp01((float)stage / crop.DaysToMature) : 1f;
        float scaleFactor = Mathf.Lerp(0.25f, 1f, t);
        transform.localScale = _baseScale * scaleFactor;

        if (_spriteRenderer != null)
        {
            Sprite picked;
            if (mature)
                picked = (_netSync.IsDepleted.Value && _depletedSprite != null) ? _depletedSprite : _readySprite;
            else
                picked = crop.GetStageSprite(stage) ?? _readySprite;
            if (picked != null) _spriteRenderer.sprite = picked;
        }
    }

    // ── Runtime configuration helpers ────────────────────────────────────────────

    public void SetHarvestOutputsRuntime(List<HarvestOutputEntry> entries) => _harvestOutputs = entries ?? new List<HarvestOutputEntry>();
    public void SetMaxHarvestCountRuntime(int n) => _maxHarvestCount = n;
    public void SetIsDepletableRuntime(bool b) => _isDepletable = b;
    public void SetRespawnDelayDaysRuntime(int d) => _respawnDelayDays = d;
    public void SetDestructionFieldsRuntime(List<HarvestOutputEntry> entries, float duration)
    {
        _destructionOutputs = entries ?? new List<HarvestOutputEntry>();
        _destructionDuration = duration;
    }

#if UNITY_EDITOR
    public void SetOutputItemsForTests(List<ItemSO> items)
    {
        _harvestOutputs = new List<HarvestOutputEntry>(items != null ? items.Count : 0);
        if (items == null) return;
        for (int i = 0; i < items.Count; i++) _harvestOutputs.Add(new HarvestOutputEntry(items[i], 1));
    }
    public void SetRequiredHarvestToolForTests(ItemSO tool) => _requiredHarvestTool = tool;
    public void SetAllowDestructionForTests(bool b) => _allowDestruction = b;
    public void SetRequiredDestructionToolForTests(ItemSO tool) => _requiredDestructionTool = tool;

    [ContextMenu("DEV: Destroy via local player")]
    private void Dev_DestroyViaLocalPlayer()
    {
        var player = FindObjectOfType<PlayerController>();
        if (player == null) { Debug.LogError("[Harvestable] No PlayerController in scene."); return; }
        var character = player.GetComponent<Character>();
        var held = GetHeldItemSO(character);
        if (!CanDestroyWith(held))
        {
            Debug.LogWarning("[Harvestable] Player can't destroy this — wrong tool or _allowDestruction is false.");
            return;
        }
        character.CharacterActions.ExecuteAction(new CharacterAction_DestroyHarvestable(character, this));
    }
#endif
}
