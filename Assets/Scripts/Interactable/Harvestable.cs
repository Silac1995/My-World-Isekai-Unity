using System.Collections.Generic;
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

    /// <summary>Can this object be harvested?</summary>
    public bool CanHarvest() => !_isDepleted && _outputItems.Count > 0;

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
    /// </summary>
    public override void Interact(Character interactor)
    {
        if (interactor == null || !CanHarvest()) return;
        if (interactor.CharacterActions == null) return;

        var gatherAction = new CharacterHarvestAction(interactor, this);
        interactor.CharacterActions.ExecuteAction(gatherAction);
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
    /// Depletes the resource. Hides the visuals and subscribes to the new-day event.
    /// </summary>
    private void Deplete()
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
}
