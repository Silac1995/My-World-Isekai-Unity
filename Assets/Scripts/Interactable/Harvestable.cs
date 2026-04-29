using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Interactables;

/// <summary>
/// Harvestable object in the world (tree, rock, ore vein, ...).
/// Inherits InteractableObject to be interactive.
/// Produces a list of ItemSO when a character harvests it.
/// Can be depleted after a number of harvests and respawn after a delay.
/// </summary>
public class Harvestable : InteractableObject
{
    [Header("Harvestable")]
    [SerializeField] private HarvestableCategory _category = HarvestableCategory.Wood;
    [SerializeField] private List<HarvestOutputEntry> _harvestOutputs = new List<HarvestOutputEntry>();
    [SerializeField] private float _harvestDuration = 3f;
    [SerializeField] private bool _isDepletable = true;
    [SerializeField] private int _maxHarvestCount = 5;
    [SerializeField, Tooltip("Number of in-game days before the resource respawns")]
    private int _respawnDelayDays = 1;

    [Header("Yield (the default 'pick' interaction)")]
    [Tooltip("If null, bare hands (or any held item) work for the yield path.")]
    [SerializeField] private ItemSO _requiredHarvestTool;

    [Header("Destruction (axe / pickaxe etc.)")]
    [SerializeField] private bool _allowDestruction;
    [SerializeField] private ItemSO _requiredDestructionTool;
    [SerializeField] private List<HarvestOutputEntry> _destructionOutputs = new List<HarvestOutputEntry>();
    [SerializeField] private float _destructionDuration = 3f;

    private int _currentHarvestCount = 0;
    private bool _isDepleted = false;
    private int _targetRespawnDay = 0;

    public event System.Action<Harvestable> OnRespawned;

    // Visuals (optional)
    [Header("Visuals")]
    [SerializeField] private GameObject _visualRoot;

    /// <summary>Items this object can produce, with per-item drop counts.</summary>
    public IReadOnlyList<HarvestOutputEntry> HarvestOutputs => _harvestOutputs;

    /// <summary>Resource category used for filtering</summary>
    public HarvestableCategory Category => _category;

    /// <summary>Time required for a single harvest</summary>
    public float HarvestDuration => _harvestDuration;

    /// <summary>Is the object depleted?</summary>
    public bool IsDepleted => _isDepleted;

    /// <summary>
    /// Remaining yield this harvestable can produce. int.MaxValue if non-depletable.
    /// </summary>
    public int RemainingYield => _isDepletable
        ? Mathf.Max(0, _maxHarvestCount - _currentHarvestCount)
        : int.MaxValue;

    /// <summary>Tool required for the yield (Pick) path; null = no tool requirement.</summary>
    public ItemSO RequiredHarvestTool => _requiredHarvestTool;

    /// <summary>True if this harvestable opts in to destruction (chop / mine to remove).
    /// Virtual so subclasses (e.g. CropHarvestable) can derive the value from a replicated
    /// source — `_allowDestruction` is a server-only runtime mutation for crops, so a client
    /// reading the base field would always see false.</summary>
    public virtual bool AllowDestruction => _allowDestruction;

    /// <summary>Tool required for the destruction path; null = any tool (when destruction is allowed).
    /// Virtual for the same reason as <see cref="AllowDestruction"/>.</summary>
    public virtual ItemSO RequiredDestructionTool => _requiredDestructionTool;

    /// <summary>Items spawned when this harvestable is destroyed, with per-item counts.</summary>
    public IReadOnlyList<HarvestOutputEntry> DestructionOutputs => _destructionOutputs;

    /// <summary>Duration of the destruction action.</summary>
    public float DestructionDuration => _destructionDuration;

    /// <summary>Can this object be harvested? Virtual so CropHarvestable can add the
    /// "must be mature" check on top of the base `!depleted && has-output` rule.</summary>
    public virtual bool CanHarvest() => !_isDepleted && HasAnyEntryWithItem(_harvestOutputs);

    private static bool HasAnyEntryWithItem(List<HarvestOutputEntry> entries)
    {
        if (entries == null) return false;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Item != null && entries[i].Count > 0) return true;
        return false;
    }

    /// <summary>Yield-path predicate that takes the held tool into account.</summary>
    public bool CanHarvestWith(ItemSO heldItem)
    {
        if (!CanHarvest()) return false;
        return _requiredHarvestTool == null || heldItem == _requiredHarvestTool;
    }

    /// <summary>Destruction-path predicate. Always false unless AllowDestruction is set.
    /// Reads via the virtual properties so subclasses overriding <see cref="AllowDestruction"/>
    /// or <see cref="RequiredDestructionTool"/> are honored on every peer.</summary>
    public bool CanDestroyWith(ItemSO heldItem)
    {
        if (!AllowDestruction) return false;
        var tool = RequiredDestructionTool;
        return tool == null || heldItem == tool;
    }

    /// <summary>
    /// Checks whether this object produces a specific item.
    /// Used by harvesters to find compatible zones.
    /// </summary>
    public bool HasOutput(ItemSO item)
    {
        if (item == null) return false;
        for (int i = 0; i < _harvestOutputs.Count; i++)
            if (_harvestOutputs[i].Item == item) return true;
        return false;
    }

    /// <summary>
    /// Checks whether this object produces at least one of the items in the list.
    /// </summary>
    public bool HasAnyOutput(List<ItemSO> items)
    {
        if (items == null) return false;
        foreach (var item in items)
        {
            if (HasOutput(item)) return true;
        }
        return false;
    }

    /// <summary>
    /// Interaction: launches a CharacterHarvestAction to harvest with animation and duration.
    /// Yield-only — the destruction path is offered through the Hold-E menu.
    /// </summary>
    public override void Interact(Character interactor)
    {
        if (interactor == null || interactor.CharacterActions == null) return;
        var held = GetHeldItemSO(interactor);

        if (CanHarvestWith(held))
        {
            var gatherAction = new CharacterHarvestAction(interactor, this);
            interactor.CharacterActions.ExecuteAction(gatherAction);
        }
        // Else: no-op. Player can hold E to see the menu.
    }

    /// <summary>
    /// Returns the rows shown by UI_InteractionMenu on Hold-E. Includes greyed-out
    /// (unavailable) rows so the player learns what tools unlock what.
    /// </summary>
    public virtual IList<HarvestInteractionOption> GetInteractionOptions(Character actor)
    {
        var list = new List<HarvestInteractionOption>(2);
        var held = GetHeldItemSO(actor);

        // --- Yield row (always present if there are output items) ---
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
                : (_requiredDestructionTool != null
                    ? $"Requires {_requiredDestructionTool.ItemName}"
                    : "Cannot destroy");
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

    /// <summary>
    /// Server-only. Commits one harvest pass: increments the harvest counter, calls Deplete
    /// when it hits the max, and returns the entries the caller should drop as WorldItems
    /// (each entry's Item × Count). Returns null on failure (depleted, no outputs, null harvester).
    /// </summary>
    public IReadOnlyList<HarvestOutputEntry> Harvest(Character harvester)
    {
        if (harvester == null || !CanHarvest()) return null;
        if (_harvestOutputs == null || _harvestOutputs.Count == 0) return null;

        _currentHarvestCount++;

        NotifyHarvesterQuestProgress(harvester);

        if (_isDepletable && _currentHarvestCount >= _maxHarvestCount)
        {
            Deplete();
        }

        Debug.Log($"<color=green>[Harvest]</color> {harvester.CharacterName} harvested from {gameObject.name} ({_harvestOutputs.Count} entry types). _isDepleted={_isDepleted}.");
        return _harvestOutputs;
    }

    /// <summary>
    /// Server-only. Spawns destruction outputs as WorldItems and despawns this harvestable.
    /// Called by the destruction CharacterAction.
    /// </summary>
    public void DestroyForOutputs()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;

        for (int i = 0; i < _destructionOutputs.Count; i++)
        {
            var entry = _destructionOutputs[i];
            if (entry.Item == null || entry.Count <= 0) continue;
            for (int n = 0; n < entry.Count; n++)
                SpawnDestructionItem(entry.Item);
        }

        OnDestroyed();

        // Use the GameObject's NetworkObject (Harvestable is MonoBehaviour, not NetworkBehaviour).
        if (TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
        {
            netObj.Despawn();
        }
        else
        {
            // Offline / non-networked harvestable: just destroy.
            Destroy(gameObject);
        }
    }

    /// <summary>Hook for subclasses (e.g. CropHarvestable clears the cell). No-op base.</summary>
    protected virtual void OnDestroyed() { }

    /// <summary>Hook for subclasses to react to depletion. No-op base.</summary>
    protected virtual void OnDepleted() { }

    /// <summary>
    /// Restore "ready" state without re-running Respawn(). Used by perennial refill paths
    /// where the cell encoding is the source of truth, so we don't need the base
    /// respawn pipeline (visual swap + OnRespawned event).
    /// </summary>
    protected void ResetHarvestState()
    {
        _currentHarvestCount = 0;
        _isDepleted = false;
        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        if (_visualRoot != null)
            _visualRoot.SetActive(true);
    }

    /// <summary>
    /// Mark depleted on a fresh spawn (post-load reconstruction) WITHOUT firing OnDepleted,
    /// scheduling respawn, or running visual-root toggling. Owner of the visual swap is the
    /// caller (e.g. perennial harvestable that drives its own visual state).
    /// </summary>
    protected void MarkDepletedNoCallback()
    {
        _isDepleted = true;
        _currentHarvestCount = _maxHarvestCount;
        // _visualRoot intentionally not toggled — perennial harvestable owns its own visual swap.
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

    /// <summary>
    /// Resolves the item the actor is currently holding in their hands.
    /// CharacterEquipment exposes the carried item via the HandsController on the
    /// character's visual rig — there is no flat 'GetActiveHandItem' helper.
    /// </summary>
    internal static ItemSO GetHeldItemSO(Character actor)
    {
        if (actor == null) return null;
        var hands = actor.CharacterVisual != null && actor.CharacterVisual.BodyPartsController != null
            ? actor.CharacterVisual.BodyPartsController.HandsController
            : null;
        return hands != null && hands.CarriedItem != null ? hands.CarriedItem.ItemSO : null;
    }

    private void SpawnDestructionItem(ItemSO item)
    {
        if (item == null) return;
        var pos = transform.position + Random.insideUnitSphere * 0.5f;
        pos.y = transform.position.y;
        WorldItem.SpawnWorldItem(item, pos);
    }

    /// <summary>
    /// Depletes the resource. Hides the visuals and subscribes to the new-day event.
    /// </summary>
    protected virtual void Deplete()
    {
        _isDepleted = true;

        ScheduleRespawnAfterDeplete();

        if (_visualRoot != null)
            _visualRoot.SetActive(false);

        Debug.Log($"<color=orange>[Harvest]</color> {gameObject.name} is depleted. Respawn scheduled for day {_targetRespawnDay}.");

        OnDepleted();
    }

    /// <summary>
    /// Hook for subclasses that own their own refill cycle (e.g. CropHarvestable, where
    /// FarmGrowthSystem owns the perennial regrow timing). Default: subscribe to OnNewDay
    /// so the resource auto-respawns after _respawnDelayDays. Override to no-op when the
    /// subclass drives respawn externally.
    /// </summary>
    protected virtual void ScheduleRespawnAfterDeplete()
    {
        if (MWI.Time.TimeManager.Instance != null)
        {
            _targetRespawnDay = MWI.Time.TimeManager.Instance.CurrentDay + _respawnDelayDays;
            MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        }
    }

    private void HandleNewDay()
    {
        if (MWI.Time.TimeManager.Instance != null && MWI.Time.TimeManager.Instance.CurrentDay >= _targetRespawnDay)
        {
            Respawn();
        }
    }

    /// <summary>
    /// Respawns the resource. Restores the visuals and resets the counter to zero.
    /// </summary>
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
    }

    private void OnDestroy()
    {
        if (_isDepleted && MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }

    // Runtime configuration helpers — used by subclasses (e.g. CropHarvestable) to drive
    // a procedurally-spawned Harvestable from data without poking serialized fields directly.
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
        character.CharacterActions.ExecuteAction(
            new CharacterAction_DestroyHarvestable(character, this));
    }
#endif
}
