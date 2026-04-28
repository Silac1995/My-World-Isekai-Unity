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
    [SerializeField] private List<ItemSO> _outputItems = new List<ItemSO>();
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
    [SerializeField] private List<ItemSO> _destructionOutputs = new List<ItemSO>();
    [SerializeField] private int _destructionOutputCount = 1;
    [SerializeField] private float _destructionDuration = 3f;

    private int _currentHarvestCount = 0;
    private bool _isDepleted = false;
    private int _targetRespawnDay = 0;

    public event System.Action<Harvestable> OnRespawned;

    // Visuals (optional)
    [Header("Visuals")]
    [SerializeField] private GameObject _visualRoot;

    /// <summary>Items this object can produce</summary>
    public IReadOnlyList<ItemSO> OutputItems => _outputItems;

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

    /// <summary>True if this harvestable opts in to destruction (chop / mine to remove).</summary>
    public bool AllowDestruction => _allowDestruction;

    /// <summary>Tool required for the destruction path; null = any tool (when destruction is allowed).</summary>
    public ItemSO RequiredDestructionTool => _requiredDestructionTool;

    /// <summary>Items spawned when this harvestable is destroyed.</summary>
    public IReadOnlyList<ItemSO> DestructionOutputs => _destructionOutputs;

    /// <summary>How many times each destruction output is spawned.</summary>
    public int DestructionOutputCount => _destructionOutputCount;

    /// <summary>Duration of the destruction action.</summary>
    public float DestructionDuration => _destructionDuration;

    /// <summary>Can this object be harvested?</summary>
    public bool CanHarvest() => !_isDepleted && _outputItems.Count > 0;

    /// <summary>Yield-path predicate that takes the held tool into account.</summary>
    public bool CanHarvestWith(ItemSO heldItem)
    {
        if (!CanHarvest()) return false;
        return _requiredHarvestTool == null || heldItem == _requiredHarvestTool;
    }

    /// <summary>Destruction-path predicate. Always false unless AllowDestruction is set.</summary>
    public bool CanDestroyWith(ItemSO heldItem)
    {
        if (!_allowDestruction) return false;
        return _requiredDestructionTool == null || heldItem == _requiredDestructionTool;
    }

    /// <summary>
    /// Checks whether this object produces a specific item.
    /// Used by harvesters to find compatible zones.
    /// </summary>
    public bool HasOutput(ItemSO item)
    {
        if (item == null) return false;
        return _outputItems.Contains(item);
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
        if (_outputItems != null && _outputItems.Count > 0)
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
                label: $"Pick {_outputItems[0].ItemName}",
                icon: _outputItems[0].Icon,
                outputPreview: $"{_maxHarvestCount}× {_outputItems[0].ItemName}",
                isAvailable: yieldOk,
                unavailableReason: yieldReason,
                actionFactory: ch => new CharacterHarvestAction(ch, this)));
        }

        /*
        // Destruction row activated once CharacterAction_DestroyHarvestable type exists
        // (it lives in Assembly-CSharp / no Pure asmdef boundary).
        if (_allowDestruction && _destructionOutputs.Count > 0)
        {
            bool destroyOk = CanDestroyWith(held);
            string destroyReason = destroyOk ? null
                : (_requiredDestructionTool != null
                    ? $"Requires {_requiredDestructionTool.ItemName}"
                    : "Cannot destroy");
            list.Add(new HarvestInteractionOption(
                label: "Destroy",
                icon: _destructionOutputs[0].Icon,
                outputPreview: $"{_destructionOutputCount}× {_destructionOutputs[0].ItemName}",
                isAvailable: destroyOk,
                unavailableReason: destroyReason,
                actionFactory: ch => new CharacterAction_DestroyHarvestable(ch, this)));
        }
        */

        return list;
    }

    /// <summary>
    /// Harvests and spawns the item as a WorldItem in the world.
    /// Returns the harvested ItemSO (null on failure).
    /// </summary>
    public ItemSO Harvest(Character harvester)
    {
        if (harvester == null || !CanHarvest()) return null;

        ItemSO harvestedItem = GetRandomOutput();
        if (harvestedItem == null) return null;

        _currentHarvestCount++;

        // Quest progress: if the harvester has an active HarvestResourceTask whose
        // target IS this harvestable, record one unit of progress so the HUD updates
        // live (and the quest auto-completes when TotalProgress >= Required). Server-only
        // because Harvest itself is server-only (CharacterActions.ApplyHarvestOnServer
        // gates this with `if (IsSpawned && !IsServer) return null;`).
        //
        // Recorded BEFORE Deplete so the final harvest goes through the "Completed" state
        // path (TotalProgress >= Required while IsValid still true) instead of "Expired"
        // (IsValid flips false the moment we Deplete). End-user impact is the same — the
        // quest disappears from the log either way — but Completed is the natural state.
        NotifyHarvesterQuestProgress(harvester);

        if (_isDepletable && _currentHarvestCount >= _maxHarvestCount)
        {
            Deplete();
        }

        Debug.Log($"<color=green>[Harvest]</color> {harvester.CharacterName} harvested {harvestedItem.ItemName}.");
        return harvestedItem;
    }

    /// <summary>
    /// Server-only. Spawns destruction outputs as WorldItems and despawns this harvestable.
    /// Called by the destruction CharacterAction.
    /// </summary>
    public void DestroyForOutputs()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;

        for (int i = 0; i < _destructionOutputCount; i++)
        {
            for (int j = 0; j < _destructionOutputs.Count; j++)
            {
                SpawnDestructionItem(_destructionOutputs[j]);
            }
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
    /// Returns a random item from the outputs.
    /// </summary>
    private ItemSO GetRandomOutput()
    {
        if (_outputItems.Count == 0) return null;
        return _outputItems[Random.Range(0, _outputItems.Count)];
    }

    /// <summary>
    /// Resolves the item the actor is currently holding in their hands.
    /// CharacterEquipment exposes the carried item via the HandsController on the
    /// character's visual rig — there is no flat 'GetActiveHandItem' helper.
    /// </summary>
    private static ItemSO GetHeldItemSO(Character actor)
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

        if (MWI.Time.TimeManager.Instance != null)
        {
            _targetRespawnDay = MWI.Time.TimeManager.Instance.CurrentDay + _respawnDelayDays;
            MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        }

        if (_visualRoot != null)
            _visualRoot.SetActive(false);

        Debug.Log($"<color=orange>[Harvest]</color> {gameObject.name} is depleted. Respawn scheduled for day {_targetRespawnDay}.");

        OnDepleted();
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
    public void SetOutputItemsRuntime(List<ItemSO> items) => _outputItems = items;
    public void SetMaxHarvestCountRuntime(int n) => _maxHarvestCount = n;
    public void SetIsDepletableRuntime(bool b) => _isDepletable = b;
    public void SetRespawnDelayDaysRuntime(int d) => _respawnDelayDays = d;
    public void SetDestructionFieldsRuntime(IReadOnlyList<ItemSO> outputs, int count, float duration)
    {
        _destructionOutputs = new List<ItemSO>(outputs);
        _destructionOutputCount = count;
        _destructionDuration = duration;
    }

#if UNITY_EDITOR
    public void SetOutputItemsForTests(List<ItemSO> items) => _outputItems = items;
    public void SetRequiredHarvestToolForTests(ItemSO tool) => _requiredHarvestTool = tool;
    public void SetAllowDestructionForTests(bool b) => _allowDestruction = b;
    public void SetRequiredDestructionToolForTests(ItemSO tool) => _requiredDestructionTool = tool;
#endif
}
