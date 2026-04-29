using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Abstract base class for commercial buildings.
/// Each commercial building type (Bar, Shop, ...) inherits from this class
/// and overrides InitializeJobs() to define its work positions.
/// </summary>
[RequireComponent(typeof(BuildingTaskManager))]
[RequireComponent(typeof(BuildingLogisticsManager))]
public abstract class CommercialBuilding : Building
{
    /// <summary>
    /// One entry of <see cref="_defaultFurnitureLayout"/> — a furniture item the server spawns
    /// as a child of the building when the building first comes into existence in a fresh world
    /// (i.e. via <c>BuildingPlacementManager</c>, NOT via save-restore).
    ///
    /// Use this — NOT nested PrefabInstances inside the building prefab — for any furniture whose
    /// prefab carries a NetworkObject. Nesting a network-bearing furniture instance inside a
    /// runtime-spawned building prefab makes NGO half-register the child during the parent's
    /// spawn, leaving a broken NetworkObject in <c>SpawnManager.SpawnedObjectsList</c> that
    /// NRE's during the next client-sync (see wiki/gotchas/host-progressive-freeze-debug-log-spam.md
    /// neighbouring entries on half-spawned NOs, and .agent/skills/multiplayer/SKILL.md §10).
    /// </summary>
    [System.Serializable]
    public class DefaultFurnitureSlot
    {
        [Tooltip("FurnitureItemSO whose InstalledFurniturePrefab will be Instantiate+Spawn'd as a child of the building.")]
        public FurnitureItemSO ItemSO;

        [Tooltip("Position relative to the building root, in building-local space. Used to compute the world spawn position; the room's FurnitureGrid then snaps + occupies the matching cell.")]
        public Vector3 LocalPosition;

        [Tooltip("Rotation relative to the building root, in building-local space (Euler degrees).")]
        public Vector3 LocalEulerAngles;

        [Tooltip("Room whose FurnitureManager owns and grid-registers this furniture. Mirror of the legacy nested-prefab parenting (e.g. Forge/Room_Main/CraftingStation → set TargetRoom to Room_Main). Required for any furniture that should appear on a room's FurnitureGrid; if left null the furniture spawns parented directly under the building root and is NOT grid-registered.")]
        public Room TargetRoom;
    }

    [Header("Commercial")]
    [SerializeField] protected Community _ownerCommunity; // Collective owner
    [SerializeField] protected Zone _storageZone;

    [Tooltip("Furniture spawned automatically by the server when this building first comes into existence in a fresh world.\n" +
             "Skipped on save-restore — restored buildings reuse their persisted furniture state.\n" +
             "Use this for any furniture whose prefab carries a NetworkObject; nesting a network-bearing furniture\n" +
             "PrefabInstance directly inside the building prefab half-spawns the child and NRE's NGO sync.")]
    [SerializeField] private List<DefaultFurnitureSlot> _defaultFurnitureLayout = new List<DefaultFurnitureSlot>();

    /// <summary>
    /// Set true after <see cref="TrySpawnDefaultFurniture"/> runs so multiple OnNetworkSpawn
    /// invocations (rare, e.g. domain reload during a session) cannot duplicate the layout.
    /// Not networked — clients never spawn furniture; this flag is server-only state.
    /// </summary>
    private bool _defaultFurnitureSpawned;

    [Tooltip("Optional outbound staging zone. When authored, the building's Logistics Manager " +
             "moves reserved ItemInstances from StorageZone into this zone before an incoming " +
             "transporter arrives, and transporters path to this zone instead of the raw " +
             "WorldItem position. Leave empty to keep legacy behaviour (transporter walks " +
             "straight into the StorageZone). Prefer a small area adjacent to the building's " +
             "exterior door — authored, not networked, so no NetworkVariable cost.")]
    [SerializeField] protected Zone _pickupZone;

    protected List<Job> _jobs = new List<Job>();
    protected List<ItemInstance> _inventory = new List<ItemInstance>();

    /// <summary>
    /// Server-authoritative replicated worker assignments, parallel to <see cref="_jobs"/> by index.
    /// Empty string = unassigned. Clients mirror each entry into their local Job._worker via
    /// <see cref="HandleJobWorkerIdChanged"/>, so Job.IsAssigned / Job.Worker evaluate consistently
    /// on every peer. Without this, the plain C# field `Job._worker` is only set on the server
    /// and the hold-E menu (plus any other client-side query) would see every slot as vacant.
    /// </summary>
    private NetworkList<FixedString64Bytes> _jobWorkerIds;

    /// <summary>
    /// Server-authoritative replicated roster of workers currently on shift (punched in),
    /// stored by <c>Character.CharacterId</c>. Single source of truth for both server and
    /// client — previously a server-only <c>List&lt;Character&gt;</c> mirror existed in
    /// parallel, but it made <see cref="ActiveWorkersOnShift"/> return an empty list on
    /// clients and caused the Time Clock UI / debug overlay / BT checks to see the wrong
    /// shift state. The public <see cref="ActiveWorkersOnShift"/> property now materialises
    /// Character references from this NetworkList on demand so every peer sees the same data.
    /// Server writes via <c>WorkerStartingShift</c> / <c>WorkerEndingShift</c>; clients are
    /// read-only.
    /// </summary>
    private NetworkList<FixedString64Bytes> _activeWorkerIds;

    /// <summary>
    /// Server-authoritative replicated count-only mirror of <see cref="_inventory"/>: one entry
    /// per <c>ItemInstance</c>, holding its <c>ItemSO.ItemId</c>. Lets <see cref="GetItemCount"/>
    /// return the correct number on clients without serialising every full <c>ItemInstance</c>
    /// (colors, custom names, etc. — that would require a much bigger refactor of the abstract
    /// ItemInstance class to be NGO-serialisable).
    ///
    /// Server writes from <see cref="AddToInventory"/> / <see cref="TakeFromInventory"/> /
    /// <see cref="RemoveExactItemFromInventory"/> / <see cref="RefreshStorageInventory"/>.
    /// Clients are read-only.
    ///
    /// Why we need this: previously <c>_inventory</c> was a plain server-only <c>List&lt;ItemInstance&gt;</c>;
    /// clients always saw an empty inventory and the building debug UI / shop counts / craft-availability
    /// checks would render zero on the client peer.
    /// </summary>
    private NetworkList<FixedString64Bytes> _inventoryItemIds;

    /// <summary>
    /// Client-side pending binds. Populated when the replicated list arrives before the
    /// referenced Character instance has spawned on this peer (late-join / map wake-up).
    /// Resolved via <see cref="HandleCharacterSpawnedForJobWorkerBind"/>.
    /// </summary>
    private readonly Dictionary<int, string> _pendingJobWorkerBinds = new Dictionary<int, string>();
    private bool _waitingForJobWorkerBinds = false;

    protected BuildingTaskManager _taskManager;
    protected BuildingLogisticsManager _logisticsManager;

    // Per-active-worker punch-in time in hours (TimeManager.CurrentTime01 * 24f).
    // Used at punch-out to compute attendance ratio for wage calculation.
    private readonly System.Collections.Generic.Dictionary<Character, float> _punchInTimeByWorker
        = new System.Collections.Generic.Dictionary<Character, float>();

    public Character Owner => _ownerIds.Count > 0 ? Character.FindByUUID(_ownerIds[0].ToString()) : null;
    public Community OwnerCommunity => _ownerCommunity;
    public IReadOnlyList<Job> Jobs => _jobs;

    /// <summary>
    /// Workers currently on shift, materialised from the replicated
    /// <see cref="_activeWorkerIds"/> NetworkList. Consistent on server AND client —
    /// every peer sees the same roster. Use this for UI ("who is punched in?"),
    /// quest eligibility, debug overlays. Use <see cref="IsWorkerOnShift"/> for
    /// allocation-free Contains-style checks.
    /// Allocates a fresh list on each call; safe for tick-rate consumers.
    /// </summary>
    public IReadOnlyList<Character> ActiveWorkersOnShift
    {
        get
        {
            if (_activeWorkerIds == null) return System.Array.Empty<Character>();
            var result = new List<Character>(_activeWorkerIds.Count);
            for (int i = 0; i < _activeWorkerIds.Count; i++)
            {
                string id = _activeWorkerIds[i].ToString();
                if (string.IsNullOrEmpty(id)) continue;
                var c = Character.FindByUUID(id);
                if (c != null) result.Add(c);
            }
            return result;
        }
    }
    public Zone StorageZone => _storageZone;
    public Zone PickupZone => _pickupZone;
    public IReadOnlyList<ItemInstance> Inventory => _inventory;

    public BuildingTaskManager TaskManager => _taskManager;
    public BuildingLogisticsManager LogisticsManager => _logisticsManager;

    // Cached once — TimeClock is a child furniture authored into the prefab/scene.
    // Rebuilds lazily when we detect the cached reference was destroyed (furniture
    // pick-up / replace). Null when the building has no clock authored yet, which
    // BTAction_Work + BTAction_PunchOut treat as a soft-fallback condition
    // (legacy "punch anywhere in BuildingZone" keeps working).
    private TimeClockFurniture _cachedTimeClock;
    public TimeClockFurniture TimeClock
    {
        get
        {
            if (_cachedTimeClock == null)
            {
                _cachedTimeClock = GetComponentInChildren<TimeClockFurniture>(includeInactive: false);
            }
            return _cachedTimeClock;
        }
    }

    /// <summary>
    /// The building is operational once all jobs are filled by a worker and construction is finished.
    /// </summary>
    public bool IsOperational => !IsUnderConstruction && _jobs.Count > 0 && _jobs.TrueForAll(j => j.IsAssigned);

    protected override void Awake()
    {
        // NetworkList must exist before base.Awake (matches Room._ownerIds pattern —
        // NGO inspects the list handle during SceneNetworkObject registration).
        _jobWorkerIds = new NetworkList<FixedString64Bytes>();
        _activeWorkerIds = new NetworkList<FixedString64Bytes>();
        _inventoryItemIds = new NetworkList<FixedString64Bytes>();

        base.Awake();

        _taskManager = gameObject.GetComponent<BuildingTaskManager>();
        if (_taskManager == null)
        {
            _taskManager = gameObject.AddComponent<BuildingTaskManager>();
        }

        _logisticsManager = gameObject.GetComponent<BuildingLogisticsManager>();
        if (_logisticsManager == null)
        {
            _logisticsManager = gameObject.AddComponent<BuildingLogisticsManager>();
        }

        InitializeJobs();
        HookQuestPublishingEvents();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Pad the replicated list to match the authored _jobs roster.
            // Every peer ran InitializeJobs() locally in Awake, so `_jobs.Count` is
            // identical across peers and the parallel-by-index scheme is safe.
            while (_jobWorkerIds.Count < _jobs.Count)
            {
                _jobWorkerIds.Add(new FixedString64Bytes(""));
            }

            TrySpawnDefaultFurniture();
        }

        _jobWorkerIds.OnListChanged += HandleJobWorkerIdChanged;
        _inventoryItemIds.OnListChanged += HandleInventoryItemIdsChanged;

        // Late-join / wake-up: the NetworkList arrives populated; walk it once to
        // materialise Job._worker on this peer. Skipped on the server because
        // AssignWorker already set _worker directly via job.Assign.
        if (!IsServer)
        {
            SyncAllJobWorkersFromList();
        }
    }

    /// <summary>
    /// Diagnostic only — fires on every peer (host + clients) when the replicated inventory
    /// count list changes. Lets us prove whether NGO replication is delivering the writes.
    /// Gated behind <see cref="DebugInventorySync"/> so it's silent in production.
    /// </summary>
    private void HandleInventoryItemIdsChanged(NetworkListEvent<FixedString64Bytes> evt)
    {
        if (!DebugInventorySync) return;
        string role = IsServer ? "Server" : "Client";
        Debug.Log($"<color=#88ddff>[InventorySync:{role}-OnChanged]</color> {buildingName}: event={evt.Type} value='{evt.Value}' (NetworkList now {_inventoryItemIds.Count} entries).");
    }

    // =========================================================================
    // DEFAULT FURNITURE SPAWN (server-only)
    // =========================================================================

    /// <summary>
    /// Server-only. Instantiates and Spawn()s entries in <see cref="_defaultFurnitureLayout"/>
    /// that don't already have a matching Furniture child on this building. The per-slot match
    /// (by FurnitureItemSO) replaces the earlier "any Furniture child present → skip all" guard,
    /// which was too aggressive: baked NetworkObject-FREE furniture (e.g. TimeClock on the Forge)
    /// is a legitimate child and would have suppressed every slot. Survives the save-restore path
    /// the same way: persisted furniture children block their corresponding slot from re-spawning,
    /// while never-baked slots still spawn on first OnNetworkSpawn.
    /// </summary>
    private void TrySpawnDefaultFurniture()
    {
        if (_defaultFurnitureSpawned) return;
        _defaultFurnitureSpawned = true;

        if (_defaultFurnitureLayout == null || _defaultFurnitureLayout.Count == 0)
        {
            Debug.Log($"[CommercialBuilding] {buildingName}: TrySpawnDefaultFurniture — layout is empty, nothing to spawn.", this);
            return;
        }

        // Snapshot existing Furniture children once. Per-slot match is by FurnitureItemSO ref.
        var existing = GetComponentsInChildren<Furniture>(includeInactive: true);
        var existingItemSOs = new System.Collections.Generic.HashSet<FurnitureItemSO>();
        foreach (var f in existing)
        {
            if (f != null && f.FurnitureItemSO != null) existingItemSOs.Add(f.FurnitureItemSO);
        }

        Debug.Log($"[CommercialBuilding] {buildingName}: TrySpawnDefaultFurniture — layout count={_defaultFurnitureLayout.Count}, existing Furniture children={existing.Length}.", this);

        for (int i = 0; i < _defaultFurnitureLayout.Count; i++)
        {
            var slot = _defaultFurnitureLayout[i];
            if (slot == null || slot.ItemSO == null || slot.ItemSO.InstalledFurniturePrefab == null)
            {
                Debug.LogWarning($"[CommercialBuilding] {buildingName}: _defaultFurnitureLayout[{i}] is missing ItemSO or InstalledFurniturePrefab — slot skipped.", this);
                continue;
            }

            if (existingItemSOs.Contains(slot.ItemSO))
            {
                Debug.Log($"[CommercialBuilding] {buildingName}: slot[{i}] '{slot.ItemSO.name}' already present as a baked or restored child — skipping spawn.", this);
                continue;
            }

            try
            {
                SpawnDefaultFurnitureSlot(slot);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        // Tier 2 cache invalidation: the just-spawned default furniture is now logically
        // owned by this building. Force the StorageFurniture / Craftable caches to refresh
        // on next access so suppliers can see new CraftingStations and storage drops can
        // see new chests within the 2 s TTL window — without waiting for it to expire.
        // See wiki/projects/optimisation-backlog.md entry #2 / D + A.
        InvalidateStorageFurnitureCache();
        if (this is CraftingBuilding crafting)
        {
            crafting.InvalidateCraftableCache();
        }
    }

    /// <summary>
    /// Server-only. Mirrors the player furniture-place flow at
    /// <c>CharacterActions.RequestFurniturePlaceServerRpc</c>: Instantiate at world position,
    /// <c>NetworkObject.Spawn()</c> as a top-level NO, then hand off to the target room's
    /// <see cref="FurnitureManager.RegisterSpawnedFurniture"/> which re-parents the furniture
    /// under the room and registers the cell occupancy on the room's <c>FurnitureGrid</c>.
    /// NGO's <c>AutoObjectParentSync</c> replicates the post-spawn re-parent to clients.
    /// </summary>
    private void SpawnDefaultFurnitureSlot(DefaultFurnitureSlot slot)
    {
        Furniture prefab = slot.ItemSO.InstalledFurniturePrefab;
        Vector3 worldPos = transform.TransformPoint(slot.LocalPosition);
        Quaternion worldRot = transform.rotation * Quaternion.Euler(slot.LocalEulerAngles);

        Furniture instance = Instantiate(prefab, worldPos, worldRot);

        var netObj = instance.GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsSpawned)
        {
            netObj.Spawn();
        }

        // Parent under the building root — the only NetworkObject in this hierarchy. Parenting
        // under Room_Main (a NetworkBehaviour on a non-NO) throws NGO's InvalidParentException;
        // the building root is the closest valid NO ancestor. Visually the furniture lives
        // inside the building at the correct world position; logical room membership is tracked
        // in the FurnitureManager._furnitures list (see RegisterSpawnedFurnitureUnchecked notes).
        instance.transform.SetParent(transform, worldPositionStays: true);

        // Use the UNCHECKED register path: default furniture is server-authored content (the level
        // designer placed the slot), not runtime user input — CanPlaceFurniture validation is for
        // the player-place flow. Unchecked register adds to grid occupancy + the room's furniture
        // list WITHOUT touching transform.parent (we already parented above).
        if (slot.TargetRoom != null && slot.TargetRoom.FurnitureManager != null)
        {
            slot.TargetRoom.FurnitureManager.RegisterSpawnedFurnitureUnchecked(instance, worldPos);
        }
        else if (slot.TargetRoom == null)
        {
            Debug.LogWarning(
                $"[CommercialBuilding] {buildingName}: default furniture slot for '{slot.ItemSO.name}' has no TargetRoom. " +
                $"Set TargetRoom on the slot so it appears in the room's FurnitureManager list.",
                this);
        }
    }

    // =========================================================================
    // QUEST PUBLISHING (Task 16 — aggregates TaskManager + LogisticsManager events)
    // =========================================================================

    /// <summary>Fires after a new IQuest has been enqueued at this building (TaskManager or OrderBook).</summary>
    public event System.Action<MWI.Quests.IQuest> OnQuestPublished;

    /// <summary>Fires when a tracked IQuest's state changes (join/leave/progress/completion).</summary>
    public event System.Action<MWI.Quests.IQuest> OnQuestStateChanged;

    private void HookQuestPublishingEvents()
    {
        if (_taskManager != null)
        {
            _taskManager.OnTaskRegistered += HandleTaskRegistered;
        }
        if (_logisticsManager != null && _logisticsManager.OrderBook != null)
        {
            _logisticsManager.OrderBook.OnBuyOrderAdded += HandleOrderPublished;
            _logisticsManager.OrderBook.OnTransportOrderAdded += HandleOrderPublished;
            _logisticsManager.OrderBook.OnCraftingOrderAdded += HandleOrderPublished;
        }
    }

    private void HandleTaskRegistered(BuildingTask task) => PublishQuest(task);
    private void HandleOrderPublished(MWI.Quests.IQuest quest) => PublishQuest(quest);

    /// <summary>Stamp issuer + world/map, subscribe to state changes, fire OnQuestPublished.</summary>
    private void PublishQuest(MWI.Quests.IQuest quest)
    {
        if (quest == null) return;

        Character issuer = ResolveIssuer();
        if (issuer != null) AssignIssuerIfPossible(quest, issuer);
        StampOriginWorldAndMap(quest);

        quest.OnStateChanged += HandleQuestStateChanged;
        OnQuestPublished?.Invoke(quest);
    }

    private void HandleQuestStateChanged(MWI.Quests.IQuest quest)
    {
        OnQuestStateChanged?.Invoke(quest);

        // Auto-unsubscribe once the quest reaches a terminal state. Without this, the quest
        // keeps a delegate reference pointing back at this building forever (even after the
        // quest is removed from its TaskManager/OrderBook list), and the quest object itself
        // can't be GC'd. With hundreds of quests published over a long worker shift, this drove
        // a slow-but-steady accumulation that compounded with the Job.Execute hot-path allocations.
        if (quest.State == MWI.Quests.QuestState.Completed ||
            quest.State == MWI.Quests.QuestState.Abandoned ||
            quest.State == MWI.Quests.QuestState.Expired)
        {
            quest.OnStateChanged -= HandleQuestStateChanged;
        }
    }

    private static void AssignIssuerIfPossible(MWI.Quests.IQuest quest, Character issuer)
    {
        // IQuest.Issuer is declared get-only; concrete types expose public setters.
        switch (quest)
        {
            case BuildingTask bt: bt.Issuer = issuer; break;
            case BuyOrder bo: bo.Issuer = issuer; break;
            case TransportOrder to: to.Issuer = issuer; break;
            case CraftingOrder co: co.Issuer = issuer; break;
        }
    }

    private void StampOriginWorldAndMap(MWI.Quests.IQuest quest)
    {
        var mapController = GetComponentInParent<MWI.WorldSystem.MapController>();
        string mapId = mapController != null ? mapController.MapId : string.Empty;
        string worldId = string.Empty;  // TODO: source from active WorldAssociation when the singleton is available to buildings.
        switch (quest)
        {
            case BuildingTask bt: bt.StampOrigin(worldId, mapId); break;
            case BuyOrder bo: bo.OriginWorldId = worldId; bo.OriginMapId = mapId; break;
            case TransportOrder to: to.OriginWorldId = worldId; to.OriginMapId = mapId; break;
            case CraftingOrder co: co.OriginWorldId = worldId; co.OriginMapId = mapId; break;
        }
    }

    /// <summary>
    /// Resolve the Character who acts as the quest issuer for this building. Priority:
    /// 1. An assigned JobLogisticsManager worker.
    /// 2. The building's Owner (if set and alive).
    /// 3. null (system-issued — quest still functional).
    /// </summary>
    private Character ResolveIssuer()
    {
        var lmJob = _jobs.OfType<JobLogisticsManager>().FirstOrDefault(j => j.IsAssigned);
        if (lmJob != null && lmJob.Worker != null) return lmJob.Worker;
        if (HasOwner) return Owner;
        return null;
    }

    /// <summary>Enumerate every IQuest this building currently publishes (TaskManager + OrderBook).
    /// Returns a materialized snapshot — the underlying lists are mutated mid-iteration in many
    /// real-world consumers (e.g. <c>TryAutoClaimExistingQuests</c> calls <c>TryClaim</c> which
    /// removes the task from <c>_availableTasks</c>; spawn-time pickup-task registration during a
    /// destroy chain mutates the same list). Yield-return over the live <c>IReadOnlyList</c> wrapper
    /// would throw "Collection was modified" on the next <c>MoveNext</c>. Cost is one List allocation
    /// per call which is acceptable for a non-hot-path query API.</summary>
    public System.Collections.Generic.IEnumerable<MWI.Quests.IQuest> GetAvailableQuests()
    {
        var snapshot = new System.Collections.Generic.List<MWI.Quests.IQuest>(8);
        if (_taskManager != null)
        {
            for (int i = 0; i < _taskManager.AvailableTasks.Count; i++)
                snapshot.Add(_taskManager.AvailableTasks[i]);
        }
        if (_logisticsManager != null && _logisticsManager.OrderBook != null)
        {
            for (int i = 0; i < _logisticsManager.OrderBook.PlacedBuyOrders.Count; i++)
                snapshot.Add(_logisticsManager.OrderBook.PlacedBuyOrders[i]);
            for (int i = 0; i < _logisticsManager.OrderBook.PlacedTransportOrders.Count; i++)
                snapshot.Add(_logisticsManager.OrderBook.PlacedTransportOrders[i]);
            for (int i = 0; i < _logisticsManager.OrderBook.ActiveCraftingOrders.Count; i++)
                snapshot.Add(_logisticsManager.OrderBook.ActiveCraftingOrders[i]);
        }
        return snapshot;
    }

    /// <summary>Find a tracked IQuest by id (used by save/load reconciliation on CharacterQuestLog).</summary>
    public MWI.Quests.IQuest GetQuestById(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return null;
        foreach (var q in GetAvailableQuests())
        {
            if (q.QuestId == questId) return q;
        }
        return null;
    }

    /// <summary>
    /// Each subclass creates its own specific jobs here.
    /// e.g. BarBuilding creates one JobBarman + several JobServer.
    /// </summary>
    protected abstract void InitializeJobs();

    /// <summary>
    /// Assigns a worker to a specific job in this building. Server-only —
    /// writes to the replicated <see cref="_jobWorkerIds"/> list so clients mirror
    /// the change into their local Job._worker via the OnListChanged callback.
    /// </summary>
    public bool AssignWorker(Character worker, Job job)
    {
        if (!IsServer) return false;
        if (worker == null || job == null) return false;
        int idx = _jobs.IndexOf(job);
        if (idx < 0) return false;
        if (job.IsAssigned) return false;

        job.Assign(worker, this);

        // Pad defensively in case OnNetworkSpawn hasn't run yet (e.g. restore during spawn).
        while (_jobWorkerIds.Count <= idx)
        {
            _jobWorkerIds.Add(new FixedString64Bytes(""));
        }
        _jobWorkerIds[idx] = new FixedString64Bytes(worker.CharacterId ?? "");
        return true;
    }

    /// <summary>
    /// Retire un worker d'un job. Server-only — clears the replicated slot so clients
    /// mirror the un-assignment.
    /// </summary>
    public void RemoveWorker(Job job)
    {
        if (!IsServer) return;
        if (job == null) return;
        int idx = _jobs.IndexOf(job);
        if (idx < 0) return;

        job.Unassign();
        if (idx < _jobWorkerIds.Count)
        {
            _jobWorkerIds[idx] = new FixedString64Bytes("");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Client-side worker-assignment mirroring
    // ─────────────────────────────────────────────────────────────

    private void SyncAllJobWorkersFromList()
    {
        if (IsServer) return;
        int bound = System.Math.Min(_jobs.Count, _jobWorkerIds.Count);
        for (int i = 0; i < bound; i++)
        {
            ApplyJobWorkerIdFromList(i);
        }
    }

    private void HandleJobWorkerIdChanged(NetworkListEvent<FixedString64Bytes> evt)
    {
        // Server already has the correct local _worker reference from job.Assign — don't
        // double-apply (would at best be a no-op, at worst flip state if the event type
        // is Clear/RemoveAt and we re-materialise from a stale index).
        if (IsServer) return;
        ApplyJobWorkerIdFromList(evt.Index);
    }

    private void ApplyJobWorkerIdFromList(int idx)
    {
        if (idx < 0 || idx >= _jobs.Count || idx >= _jobWorkerIds.Count) return;
        var job = _jobs[idx];
        if (job == null) return;

        string id = _jobWorkerIds[idx].ToString();
        _pendingJobWorkerBinds.Remove(idx); // any stale pending entry for this slot is obsolete

        if (string.IsNullOrEmpty(id))
        {
            if (job.IsAssigned) job.Unassign();
            return;
        }

        Character c = Character.FindByUUID(id);
        if (c != null)
        {
            if (job.IsAssigned && job.Worker != c) job.Unassign();
            if (!job.IsAssigned) job.Assign(c, this);
            return;
        }

        // Character isn't loaded on this peer yet — queue and retry on CharacterSpawned.
        _pendingJobWorkerBinds[idx] = id;
        SubscribeJobWorkerBindListener();
    }

    private void SubscribeJobWorkerBindListener()
    {
        if (_waitingForJobWorkerBinds) return;
        Character.OnCharacterSpawned += HandleCharacterSpawnedForJobWorkerBind;
        _waitingForJobWorkerBinds = true;
    }

    private void UnsubscribeJobWorkerBindListener()
    {
        if (!_waitingForJobWorkerBinds) return;
        Character.OnCharacterSpawned -= HandleCharacterSpawnedForJobWorkerBind;
        _waitingForJobWorkerBinds = false;
    }

    private void HandleCharacterSpawnedForJobWorkerBind(Character spawned)
    {
        if (spawned == null || _pendingJobWorkerBinds.Count == 0) return;
        string spawnedId = spawned.CharacterId;
        if (string.IsNullOrEmpty(spawnedId)) return;

        List<int> resolved = null;
        foreach (var kvp in _pendingJobWorkerBinds)
        {
            if (kvp.Value == spawnedId)
            {
                (resolved ??= new List<int>()).Add(kvp.Key);
            }
        }
        if (resolved == null) return;

        foreach (int idx in resolved)
        {
            ApplyJobWorkerIdFromList(idx); // Remove-from-pending happens inside Apply.
        }

        if (_pendingJobWorkerBinds.Count == 0) UnsubscribeJobWorkerBindListener();
    }

    /// <summary>
    /// Finds the first available (unassigned) job of a given type.
    /// </summary>
    public T FindAvailableJob<T>() where T : Job
    {
        foreach (var job in _jobs)
        {
            if (job is T typedJob && !typedJob.IsAssigned)
                return typedJob;
        }
        return null;
    }

    /// <summary>
    /// Returns all jobs of a given type.
    /// </summary>
    public IEnumerable<T> GetJobsOfType<T>() where T : Job
    {
        return _jobs.OfType<T>();
    }

    /// <summary>
    /// Drives all assigned employees through one work tick.
    /// Called regularly (by the BuildingManager or by Update).
    /// </summary>
    public void UpdateWorkCycle()
    {
        foreach (var job in _jobs)
        {
            if (job.CanExecute())
            {
                job.Execute();
            }
        }
    }

    public void SetOwner(Character newOwner, Job ownerJob = null, bool autoAssignJob = true)
    {
        if (!IsServer) return;

        // Mirror the upcoming _ownerIds clear into each old owner's CharacterLocations
        // BEFORE we clear the NetworkList — otherwise we lose the ability to resolve them
        // and their OwnedBuildings would keep a dangling reference to this building.
        for (int i = _ownerIds.Count - 1; i >= 0; i--)
        {
            Character oldOwner = Character.FindByUUID(_ownerIds[i].ToString());
            if (oldOwner != null && oldOwner.CharacterLocations != null)
            {
                oldOwner.CharacterLocations.UnregisterOwnedBuilding(this);
            }
        }

        // Remove from old community
        if (_ownerCommunity != null && _ownerCommunity.ownedBuildings.Contains(this))
        {
            _ownerCommunity.ownedBuildings.Remove(this);
        }

        // Replicate owner via _ownerIds (mirror ResidentialBuilding). Server-only write;
        // clients receive the change via NetworkList replication.
        while (_ownerIds.Count > 0) _ownerIds.RemoveAt(0);
        if (newOwner != null) AddOwner(newOwner); // Inherited from Room — adds newOwner.CharacterId to _ownerIds.

        // Mirror into the new owner's CharacterLocations so OwnedBuildings stays in sync
        // with the building-side _ownerIds NetworkList.
        if (newOwner != null && newOwner.CharacterLocations != null)
        {
            newOwner.CharacterLocations.RegisterOwnedBuilding(this);
        }

        // Add to new community if applicable
        if (newOwner != null && newOwner.CharacterCommunity != null && newOwner.CharacterCommunity.CurrentCommunity != null)
        {
            _ownerCommunity = newOwner.CharacterCommunity.CurrentCommunity;
            if (!_ownerCommunity.ownedBuildings.Contains(this))
            {
                _ownerCommunity.ownedBuildings.Add(this);
            }
        }
        else
        {
            _ownerCommunity = null;
        }

        Debug.Log($"<color=green>[Building]</color> {newOwner?.CharacterName} now owns {buildingName}.");

        // Restore path passes autoAssignJob=false because the saved Employees list
        // already carries the boss's actual job (avoids the auto-pick stealing a slot
        // that another saved employee owns).
        if (!autoAssignJob) return;

        if (ownerJob == null)
        {
            // Is there ALREADY someone assigned to a JobLogisticsManager in this building?
            bool hasActiveLogisticsManager = _jobs.OfType<JobLogisticsManager>().Any(j => j.IsAssigned);

            if (!hasActiveLogisticsManager)
            {
                // If no one is doing logistics, the boss MUST take that position.
                ownerJob = _jobs.OfType<JobLogisticsManager>().FirstOrDefault();
            }

            // If a logistics manager already exists (or the building has none at all), grab any free position.
            if (ownerJob == null)
            {
                ownerJob = GetAvailableJobs().FirstOrDefault();
            }
        }

        // The boss can also take a job in their own building.
        if (ownerJob != null && newOwner != null)
        {
            var charJob = newOwner.CharacterJob;
            if (charJob != null)
            {
                charJob.TakeJob(ownerJob, this);
            }
        }
    }

    // ---------------------------------------------------------------------------------
    //  Save / Load — Owner & Employee restoration
    // ---------------------------------------------------------------------------------

    /// <summary>Owner UUIDs awaiting Character spawn (server-only).</summary>
    private readonly List<string> _pendingOwnerIds = new List<string>();
    /// <summary>Employee assignments awaiting Character spawn (server-only).</summary>
    private readonly List<EmployeeSaveEntry> _pendingEmployees = new List<EmployeeSaveEntry>();
    /// <summary>True while subscribed to <see cref="Character.OnCharacterSpawned"/>.</summary>
    private bool _waitingForCharacters = false;

    /// <summary>
    /// Server-only. Re-binds saved owner + employees back to this freshly-spawned building.
    /// Characters that aren't loaded yet are queued — a Character.OnCharacterSpawned subscription
    /// retries them until everyone is bound or the building despawns.
    ///
    /// Call once, immediately after the building is NetworkObject.Spawn()'d and its
    /// NetworkBuildingId has been overwritten with the saved GUID.
    /// </summary>
    public void RestoreFromSaveData(List<string> ownerIds, List<EmployeeSaveEntry> employees)
    {
        if (!IsServer) return;

        _pendingOwnerIds.Clear();
        _pendingEmployees.Clear();

        if (ownerIds != null)
        {
            foreach (var id in ownerIds)
            {
                if (!string.IsNullOrEmpty(id)) _pendingOwnerIds.Add(id);
            }
        }
        if (employees != null)
        {
            foreach (var e in employees)
            {
                if (e != null && !string.IsNullOrEmpty(e.CharacterId) && !string.IsNullOrEmpty(e.JobType))
                    _pendingEmployees.Add(e);
            }
        }

        if (_pendingOwnerIds.Count == 0 && _pendingEmployees.Count == 0) return;

        Debug.Log($"<color=cyan>[CommercialBuilding:Restore]</color> {buildingName}: pending owners={_pendingOwnerIds.Count}, employees={_pendingEmployees.Count}");

        // Try to resolve everything that's already loaded.
        TryResolvePending();

        // Anything left? Subscribe and let OnCharacterSpawned drive future binds.
        if ((_pendingOwnerIds.Count > 0 || _pendingEmployees.Count > 0) && !_waitingForCharacters)
        {
            Character.OnCharacterSpawned += HandleCharacterSpawnedForRestore;
            _waitingForCharacters = true;
            Debug.Log($"<color=cyan>[CommercialBuilding:Restore]</color> {buildingName}: subscribed to OnCharacterSpawned for {_pendingOwnerIds.Count} owner(s) + {_pendingEmployees.Count} employee(s).");
        }
    }

    private void HandleCharacterSpawnedForRestore(Character spawned)
    {
        if (!IsServer || spawned == null) return;
        TryResolvePending();

        if (_pendingOwnerIds.Count == 0 && _pendingEmployees.Count == 0)
        {
            UnsubscribeRestoreListener();
        }
    }

    private void TryResolvePending()
    {
        // --- Owners ---
        for (int i = _pendingOwnerIds.Count - 1; i >= 0; i--)
        {
            string id = _pendingOwnerIds[i];
            Character owner = Character.FindByUUID(id);
            if (owner == null) continue;

            // Find the owner's saved job (if any) so we can restore it directly,
            // bypassing SetOwner's auto-job-pick (which could conflict with employees).
            Job ownerJob = null;
            var ownerEmployeeEntry = _pendingEmployees.FirstOrDefault(e => e.CharacterId == id);
            if (ownerEmployeeEntry != null)
            {
                ownerJob = FindFreeJobByType(ownerEmployeeEntry.JobType);
            }

            // Apply ownership without auto-pick. If the saved data had an explicit ownerJob,
            // SetOwner's tail will route it to TakeJob.
            SetOwner(owner, ownerJob, autoAssignJob: false);

            // The TakeJob inside SetOwner only fires when ownerJob != null (and autoAssign=false),
            // so explicitly call it here when we resolved a job slot for the owner.
            if (ownerJob != null && owner.CharacterJob != null)
            {
                owner.CharacterJob.TakeJob(ownerJob, this);
                _pendingEmployees.Remove(ownerEmployeeEntry);
            }

            Debug.Log($"<color=green>[CommercialBuilding:Restore]</color> {buildingName}: bound owner '{owner.CharacterName}' (id={id}).");
            _pendingOwnerIds.RemoveAt(i);
        }

        // --- Employees ---
        for (int i = _pendingEmployees.Count - 1; i >= 0; i--)
        {
            var entry = _pendingEmployees[i];
            Character worker = Character.FindByUUID(entry.CharacterId);
            if (worker == null) continue;

            Job job = FindFreeJobByType(entry.JobType);
            if (job == null)
            {
                // Job type missing from this building's roster (data drift) —
                // log and drop so we don't keep retrying forever.
                Debug.LogWarning($"<color=orange>[CommercialBuilding:Restore]</color> {buildingName}: no free '{entry.JobType}' slot for worker '{worker.CharacterName}'. Dropping pending entry.");
                _pendingEmployees.RemoveAt(i);
                continue;
            }

            if (worker.CharacterJob != null && worker.CharacterJob.TakeJob(job, this))
            {
                Debug.Log($"<color=green>[CommercialBuilding:Restore]</color> {buildingName}: bound '{worker.CharacterName}' to {entry.JobType}.");
            }
            else
            {
                Debug.LogWarning($"<color=orange>[CommercialBuilding:Restore]</color> {buildingName}: TakeJob failed for '{worker.CharacterName}' on '{entry.JobType}' (schedule conflict?). Dropping pending entry.");
            }
            _pendingEmployees.RemoveAt(i);
        }
    }

    private Job FindFreeJobByType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        foreach (var j in _jobs)
        {
            if (j == null || j.IsAssigned) continue;
            if (j.GetType().Name == typeName) return j;
        }
        return null;
    }

    private void UnsubscribeRestoreListener()
    {
        if (!_waitingForCharacters) return;
        Character.OnCharacterSpawned -= HandleCharacterSpawnedForRestore;
        _waitingForCharacters = false;
    }

    public override void OnNetworkDespawn()
    {
        if (_jobWorkerIds != null)
        {
            _jobWorkerIds.OnListChanged -= HandleJobWorkerIdChanged;
        }
        if (_inventoryItemIds != null)
        {
            _inventoryItemIds.OnListChanged -= HandleInventoryItemIdsChanged;
        }
        UnsubscribeJobWorkerBindListener();
        UnsubscribeRestoreListener();
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Le building a-t-il un owner/boss (individuel) ?
    /// </summary>
    public bool HasOwner
    {
        get
        {
            Character o = Owner;
            return o != null && o.IsAlive();
        }
    }

    /// <summary>
    /// Checks if this building is located in a map that has a recognized Community Leader.
    /// </summary>
    public bool HasCommunityLeader()
    {
        var mapController = GetComponentInParent<MWI.WorldSystem.MapController>();
        if (mapController != null && MWI.WorldSystem.MapRegistry.Instance != null)
        {
            var comm = MWI.WorldSystem.MapRegistry.Instance.GetCommunity(mapController.MapId);
            if (comm != null && !string.IsNullOrEmpty(comm.LeaderNpcId))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A character asks the boss of this building (or the community leader) for a job.
    /// Returns true if the hire is approved.
    /// </summary>
    public bool AskForJob(Character applicant, Job job)
    {
        if (applicant == null || job == null) return false;

        // A boss is required to hire (direct boss or community leader).
        if (!HasOwner && !HasCommunityLeader())
        {
            Debug.Log($"<color=red>[Building]</color> {buildingName} has no boss or community leader. Nobody can hire here.");
            return false;
        }

        // The job must exist in this building.
        if (!_jobs.Contains(job))
        {
            Debug.Log($"<color=red>[Building]</color> The position {job.JobTitle} does not exist in {buildingName}.");
            return false;
        }

        // The job must be vacant.
        if (job.IsAssigned)
        {
            Debug.Log($"<color=orange>[Building]</color> The position {job.JobTitle} at {buildingName} is already taken.");
            return false;
        }

        // Check job-specific prerequisites (e.g. skills for an artisan).
        if (!job.CanTakeJob(applicant))
        {
            Debug.Log($"<color=orange>[Building]</color> {applicant.CharacterName} does not meet the requirements for the {job.JobTitle} position.");
            return false;
        }

        // Hire approved. Return true so CharacterJob.TakeJob() can synchronize the
        // assignment on both sides (Employee and Building).
        return true;
    }

    /// <summary>
    /// Returns all unassigned jobs in this building.
    /// </summary>
    public IEnumerable<Job> GetAvailableJobs()
    {
        return _jobs.Where(j => !j.IsAssigned);
    }

    /// <summary>
    /// Authorization predicate reused by owner-side mutation entry points
    /// (currently <see cref="TrySetAssignmentWage"/>; future owner-edit UI).
    /// Mirrors the AskForJob hiring gate: a request is authorized when the
    /// requester is either the building's direct owner OR the leader of the
    /// community whose map this building lives in.
    /// </summary>
    private bool IsAuthorizedToManage(Character requester)
    {
        if (requester == null) return false;

        // Direct owner check — reuses Room.IsOwner(Character), which compares
        // against the replicated _ownerIds NetworkList.
        if (IsOwner(requester)) return true;

        // Community-leader check — mirrors HasCommunityLeader's lookup but
        // verifies the requester's CharacterId matches the recorded leader.
        var mapController = GetComponentInParent<MWI.WorldSystem.MapController>();
        if (mapController != null && MWI.WorldSystem.MapRegistry.Instance != null)
        {
            var comm = MWI.WorldSystem.MapRegistry.Instance.GetCommunity(mapController.MapId);
            if (comm != null && !string.IsNullOrEmpty(comm.LeaderNpcId) &&
                comm.LeaderNpcId == requester.CharacterId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Server-authoritative wrapper around JobAssignment.SetWage that enforces
    /// the same owner / community-leader authorization gate used by AskForJob.
    /// Use null arguments to leave a wage field unchanged.
    /// Returns true if the change was authorized AND any field was modified.
    /// </summary>
    public bool TrySetAssignmentWage(Character requester, Character worker,
        int? pieceRate = null, int? minimumShift = null, int? fixedShift = null)
    {
        if (requester == null || worker == null)
        {
            Debug.LogError("[CommercialBuilding] TrySetAssignmentWage: null requester or worker.");
            return false;
        }

        // Server-authority guard — mirrors CharacterWallet.AddCoins (Task 7).
        // In offline/Solo (no NetworkManager listening), allow the call so the
        // single-player path keeps working.
        if (!IsServer && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            Debug.LogError("[CommercialBuilding] TrySetAssignmentWage called on non-server instance. Route through a ServerRpc.");
            return false;
        }

        if (!IsAuthorizedToManage(requester))
        {
            Debug.LogWarning($"[CommercialBuilding] TrySetAssignmentWage denied: {requester.CharacterName} is not authorized to manage {name}.");
            return false;
        }

        var charJob = worker.CharacterJob;
        if (charJob == null) return false;

        foreach (var assn in charJob.ActiveJobs)
        {
            if (assn.Workplace == this && assn.AssignedJob != null)
            {
                return assn.SetWage(pieceRate, minimumShift, fixedShift);
            }
        }

        Debug.LogWarning($"[CommercialBuilding] TrySetAssignmentWage: worker {worker.CharacterName} has no assignment at {name}.");
        return false;
    }

    /// <summary>
    /// Client → Server entry point for a player touching this building's Time Clock
    /// furniture. Server re-validates employment + resolves the worker by id, then
    /// routes to the shared <see cref="TimeClockFurnitureInteractable.RunPunchCycleServerSide"/>
    /// so player and NPC punch cycles share a single code path.
    /// NPCs never send this RPC — they live on the server and call the interactable
    /// directly via BT nodes.
    /// </summary>
    [Unity.Netcode.ServerRpc(RequireOwnership = false)]
    public void RequestPunchAtTimeClockServerRpc(string workerId)
    {
        if (string.IsNullOrEmpty(workerId)) return;
        Character worker = Character.FindByUUID(workerId);
        if (worker == null)
        {
            Debug.LogWarning($"[CommercialBuilding] RequestPunchAtTimeClockServerRpc: worker '{workerId}' not found on server.");
            return;
        }

        // Re-validate authorization server-side (defence-in-depth; client-side check
        // in TimeClockFurnitureInteractable.Interact is UX-only).
        if (!IsWorkerEmployedHere(worker))
        {
            Debug.LogWarning($"[CommercialBuilding] Punch denied: {worker.CharacterName} is not employed at {buildingName}.");
            return;
        }

        var clock = TimeClock;
        if (clock == null)
        {
            Debug.LogWarning($"[CommercialBuilding] {buildingName} has no TimeClockFurniture authored — cannot honour player punch request.");
            return;
        }

        var interactable = clock.GetComponent<TimeClockFurnitureInteractable>();
        if (interactable == null)
        {
            Debug.LogError($"[CommercialBuilding] TimeClockFurniture on {buildingName} is missing a TimeClockFurnitureInteractable sibling component.");
            return;
        }

        // Proximity gate — prevent a rogue/modded client from firing the RPC
        // from anywhere. Canonical Interactable-System rule:
        // InteractableObject.IsCharacterInInteractionZone(character) tests
        // Character.transform.position against the authored InteractionZone
        // AABB (transform rather than Rigidbody so ClientNetworkTransform's
        // latest synced value is read — rb.position can trail by a physics
        // tick on the server's kinematic rigidbody for a client-owned player).
        if (!interactable.IsCharacterInInteractionZone(worker))
        {
            var zone = interactable.InteractionZone;
            string zoneInfo = zone != null
                ? $"bounds.center={zone.bounds.center} extents={zone.bounds.extents}"
                : "zone=null";
            Debug.LogWarning(
                $"[CommercialBuilding] Punch denied: {worker.CharacterName} is not inside the Time Clock interaction zone at {buildingName}. " +
                $"worker.transform.position={worker.transform.position} " +
                $"worker.Rigidbody.position={(worker.Rigidbody != null ? worker.Rigidbody.position.ToString() : "null")} " +
                $"zone.{zoneInfo}");
            return;
        }

        interactable.RunPunchCycleServerSide(worker);
    }

    /// <summary>
    /// Employment predicate used by the punch ServerRpc + the interactable's
    /// client-side eligibility short-circuit. Reads the replicated
    /// <see cref="_jobWorkerIds"/> NetworkList so the answer is identical on
    /// server and clients — <c>CharacterJob._activeJobs</c> itself is
    /// server-only, which is why we can't rely on it for the client-side
    /// short-circuit (clients would always see "not employed" and the toast
    /// would fire before the ServerRpc could even go out).
    /// </summary>
    public bool IsWorkerEmployedHere(Character worker)
    {
        if (worker == null) return false;
        string id = worker.CharacterId;
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < _jobWorkerIds.Count; i++)
        {
            if (_jobWorkerIds[i].ToString() == id) return true;
        }
        return false;
    }

    /// <summary>
    /// Shift-presence predicate reading the replicated <see cref="_activeWorkerIds"/>
    /// NetworkList. Works on server and clients — the same list is the single source
    /// of truth for both peers, which is why the public <see cref="ActiveWorkersOnShift"/>
    /// materialiser also reads from it. Used by the Time Clock interactable to pick
    /// Punch In vs Punch Out both client-side (UI prompt) and server-side (action choice),
    /// and by BT nodes that need a cheap allocation-free containment check.
    /// </summary>
    public bool IsWorkerOnShift(Character worker)
    {
        if (worker == null) return false;
        string id = worker.CharacterId;
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < _activeWorkerIds.Count; i++)
        {
            if (_activeWorkerIds[i].ToString() == id) return true;
        }
        return false;
    }

    /// <summary>
    /// Called when an employee physically arrives at their workplace and starts the shift (Punch In).
    /// </summary>
    public virtual void WorkerStartingShift(Character worker)
    {
        // Server-authority guard (defence-in-depth). WorkerStartingShift mutates
        // server-only state (_activeWorkerIds NetworkList, _punchInTimeByWorker, quest
        // auto-claim hooks). Client-side callers must route through
        // RequestPunchAtTimeClockServerRpc. Offline / solo keeps working because
        // NetworkManager.Singleton is null OR !IsListening.
        if (!IsServer && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            Debug.LogError("[CommercialBuilding] WorkerStartingShift called on non-server instance. Route through RequestPunchAtTimeClockServerRpc.");
            return;
        }

        // Wage system hook: record punch-in time + notify worker's WorkLog.
        if (worker != null)
        {
            float nowHours = MWI.Time.TimeManager.Instance != null
                ? MWI.Time.TimeManager.Instance.CurrentTime01 * 24f
                : 0f;
            _punchInTimeByWorker[worker] = nowHours;

            var workLog = worker.CharacterWorkLog;
            if (workLog != null)
            {
                // Find the assignment for THIS worker at THIS building so we know the JobType + scheduled end.
                var charJob = worker.CharacterJob;
                if (charJob != null)
                {
                    foreach (var assn in charJob.ActiveJobs)
                    {
                        if (assn.Workplace == this && assn.AssignedJob != null)
                        {
                            var jobType = assn.AssignedJob.Type;
                            // Find the scheduled end-of-shift in 0..1 time-of-day from this assignment's WorkScheduleEntries.
                            float scheduledEndTime01 = ComputeScheduledEndTime01(assn);
                            workLog.OnPunchIn(jobType, GetBuildingIdForWorklog(), GetBuildingDisplayNameForWorklog(), scheduledEndTime01);
                            break;
                        }
                    }
                }
            }
        }

        // Add to the replicated roster (single source of truth). `IsWorkerOnShift`
        // reads the same list and is the idempotency gate — identical to the server
        // and client view.
        if (worker != null && !IsWorkerOnShift(worker))
        {
            if (string.IsNullOrEmpty(worker.CharacterId))
            {
                Debug.LogError($"[CommercialBuilding] WorkerStartingShift: {worker.CharacterName} has empty CharacterId — shift state cannot be replicated.");
                return;
            }

            _activeWorkerIds.Add(new FixedString64Bytes(worker.CharacterId));

            Debug.Log($"<color=green>[Building]</color> {worker.CharacterName} punched in at {buildingName}.");

            // Trigger the logistics logic if this is the manager.
            if (worker.CharacterJob != null)
            {
                var logisticsJob = worker.CharacterJob.ActiveJobs
                    .Select(j => j.AssignedJob)
                    .OfType<JobLogisticsManager>()
                    .FirstOrDefault(j => j.Workplace == this);

                if (logisticsJob != null)
                {
                    logisticsJob.OnWorkerPunchIn();
                }
            }

            // Quest system hook: auto-claim eligible already-published quests + subscribe for
            // future publications during this shift.
            TryAutoClaimExistingQuests(worker);
            SubscribeWorkerQuestAutoClaim(worker);
        }
    }

    // =========================================================================
    // Quest auto-claim (Task 21)
    // =========================================================================

    private readonly System.Collections.Generic.Dictionary<Character, System.Action<MWI.Quests.IQuest>> _questAutoClaimHandlers
        = new System.Collections.Generic.Dictionary<Character, System.Action<MWI.Quests.IQuest>>();

    // Per-worker set of quest IDs this building auto-claimed onto them. Used by
    // WorkerEndingShift to abandon those quests on punch-out so the worker isn't
    // left holding employer-issued work after they go off shift.
    private readonly System.Collections.Generic.Dictionary<Character, System.Collections.Generic.HashSet<string>> _workerClaimedQuestIds
        = new System.Collections.Generic.Dictionary<Character, System.Collections.Generic.HashSet<string>>();

    private void TryAutoClaimExistingQuests(Character worker)
    {
        if (worker == null || worker.CharacterQuestLog == null) return;
        foreach (var quest in GetAvailableQuests())
        {
            if (IsQuestEligibleForWorker(quest, worker))
            {
                if (worker.CharacterQuestLog.TryClaim(quest))
                {
                    RecordWorkerClaimed(worker, quest.QuestId);
                }
            }
        }
    }

    private void SubscribeWorkerQuestAutoClaim(Character worker)
    {
        if (worker == null) return;
        if (_questAutoClaimHandlers.ContainsKey(worker)) return;

        System.Action<MWI.Quests.IQuest> handler = quest => TryAutoClaimForOnShiftWorker(quest, worker);
        _questAutoClaimHandlers[worker] = handler;
        OnQuestPublished += handler;
    }

    private void UnsubscribeWorkerQuestAutoClaim(Character worker)
    {
        if (worker == null) return;
        if (_questAutoClaimHandlers.TryGetValue(worker, out var handler))
        {
            OnQuestPublished -= handler;
            _questAutoClaimHandlers.Remove(worker);
        }
    }

    private void TryAutoClaimForOnShiftWorker(MWI.Quests.IQuest quest, Character worker)
    {
        if (worker == null || worker.CharacterQuestLog == null) return;
        if (!IsQuestEligibleForWorker(quest, worker)) return;
        if (worker.CharacterQuestLog.TryClaim(quest))
        {
            RecordWorkerClaimed(worker, quest.QuestId);
        }
    }

    private void RecordWorkerClaimed(Character worker, string questId)
    {
        if (worker == null || string.IsNullOrEmpty(questId)) return;
        if (!_workerClaimedQuestIds.TryGetValue(worker, out var set))
        {
            set = new System.Collections.Generic.HashSet<string>();
            _workerClaimedQuestIds[worker] = set;
        }
        set.Add(questId);
    }

    /// <summary>
    /// Abandon every quest this building has auto-claimed onto <paramref name="worker"/>
    /// during their shift. Called from WorkerEndingShift so workers don't keep
    /// employer-issued quests on their log after punching out.
    ///
    /// Tracking-set approach (vs. walking ActiveQuests + checking issuer): correct
    /// even when GetAvailableQuests no longer reports the quest (HarvestResourceTask
    /// moves to in-progress on claim and disappears from AvailableTasks immediately).
    /// </summary>
    private void AbandonWorkerClaimedQuests(Character worker)
    {
        if (worker == null || worker.CharacterQuestLog == null) return;
        if (!_workerClaimedQuestIds.TryGetValue(worker, out var set) || set.Count == 0)
        {
            _workerClaimedQuestIds.Remove(worker);
            return;
        }

        // Snapshot ids so we don't mutate the set we're iterating, and we can drop
        // it whole at the end.
        var ids = new System.Collections.Generic.List<string>(set);
        foreach (var questId in ids)
        {
            // Find the live quest in the worker's active list (server only — client
            // path is no-op here because punch-out runs on the server).
            MWI.Quests.IQuest match = null;
            foreach (var q in worker.CharacterQuestLog.ActiveQuests)
            {
                if (q != null && q.QuestId == questId) { match = q; break; }
            }
            if (match != null) worker.CharacterQuestLog.TryAbandon(match);
        }
        _workerClaimedQuestIds.Remove(worker);
    }

    private bool IsQuestEligibleForWorker(MWI.Quests.IQuest quest, Character worker)
    {
        if (quest == null || worker == null) return false;
        if (quest.State != MWI.Quests.QuestState.Open) return false;

        var charJob = worker.CharacterJob;
        if (charJob == null) return false;
        foreach (var assn in charJob.ActiveJobs)
        {
            if (assn.Workplace == this && assn.AssignedJob != null)
            {
                return DoesJobTypeAcceptQuest(assn.AssignedJob.Type, quest);
            }
        }
        return false;
    }

    private static bool DoesJobTypeAcceptQuest(MWI.WorldSystem.JobType jobType, MWI.Quests.IQuest quest)
    {
        // v1 mapping — harvest tasks to harvester family, pickup/buy to logistics manager,
        // transport to transporter, craft to crafter family. Refine as new jobs land.
        if (quest is HarvestResourceTask || quest is DestroyHarvestableTask)
        {
            // Both yield-path harvest tasks AND destruction-path destroy tasks (e.g. chop
            // an apple tree for wood, mine an ore vein for iron) accept the same harvester
            // job family. The destination tool differs but the assignment-eligibility
            // rule is the same — these jobs work resource nodes.
            return jobType == MWI.WorldSystem.JobType.Woodcutter
                || jobType == MWI.WorldSystem.JobType.Miner
                || jobType == MWI.WorldSystem.JobType.Forager
                || jobType == MWI.WorldSystem.JobType.Farmer;
        }
        if (quest is PickupLooseItemTask)
            return jobType == MWI.WorldSystem.JobType.LogisticsManager
                || jobType == MWI.WorldSystem.JobType.Transporter;
        if (quest is BuyOrder)
            return jobType == MWI.WorldSystem.JobType.LogisticsManager;
        if (quest is TransportOrder)
            return jobType == MWI.WorldSystem.JobType.Transporter;
        if (quest is CraftingOrder)
            return jobType == MWI.WorldSystem.JobType.Crafter
                || jobType == MWI.WorldSystem.JobType.Blacksmith
                || jobType == MWI.WorldSystem.JobType.BlacksmithApprentice;
        return false;
    }

    /// <summary>
    /// Returns a worker's work position inside this building.
    /// By default, returns a random point inside the building zone.
    /// Subclasses (e.g. ShopBuilding) can override to return a precise station.
    /// </summary>
    public virtual Vector3 GetWorkPosition(Character worker)
    {
        // Get a base position (building zone or building center).
        Vector3 basePos = GetRandomPointInBuildingZone(worker.transform.position.y);

        // Add a small worker-ID-based offset so everyone does not converge on the
        // exact same point when the zone is too small.
        float offsetRange = 1.5f;
        float offsetX = (Mathf.Abs(worker.gameObject.GetInstanceID() % 100) / 50f - 1f) * offsetRange;
        float offsetZ = (Mathf.Abs((worker.gameObject.GetInstanceID() / 100) % 100) / 50f - 1f) * offsetRange;

        Vector3 offsetPos = basePos + new Vector3(offsetX, 0, offsetZ);

        // Verify the offset point is still valid on the NavMesh.
        if (UnityEngine.AI.NavMesh.SamplePosition(offsetPos, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }

        return basePos;
    }

    /// <summary>
    /// Called when an employee leaves their work behaviour
    /// (end of day, special event) (Punch Out).
    /// </summary>
    public virtual void WorkerEndingShift(Character worker)
    {
        // Server-authority guard — mirror WorkerStartingShift. Defence-in-depth;
        // CharacterWallet.AddCoins also guards wage payment, but bailing early keeps
        // _activeWorkerIds + WorkLog writes consistent across peers.
        if (!IsServer && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
        {
            Debug.LogError("[CommercialBuilding] WorkerEndingShift called on non-server instance. Route through RequestPunchAtTimeClockServerRpc.");
            return;
        }

        // Wage system hook: finalize WorkLog shift + compute & pay wage.
        if (worker != null)
        {
            // Look up the assignment for this worker at this building.
            // NOTE v1 limitation: if a worker holds multiple roles at the same building,
            // only the FIRST matching assignment gets paid this shift.
            var charJob = worker.CharacterJob;
            JobAssignment matchingAssignment = null;
            if (charJob != null)
            {
                foreach (var assn in charJob.ActiveJobs)
                {
                    if (assn.Workplace == this && assn.AssignedJob != null)
                    {
                        matchingAssignment = assn;
                        break;
                    }
                }
            }

            if (matchingAssignment != null)
            {
                var workLog = worker.CharacterWorkLog;
                var jobType = matchingAssignment.AssignedJob.Type;
                var summary = workLog != null
                    ? workLog.FinalizeShift(jobType, GetBuildingIdForWorklog())
                    : new ShiftSummary();

                // Compute hours worked and scheduled length.
                float scheduledShiftHours = ComputeScheduledShiftHours(matchingAssignment);
                float hoursWorked = ComputeHoursWorked(worker);

                var paymentCurrency = matchingAssignment.Currency.Id == 0
                    ? MWI.Economy.CurrencyId.Default
                    : matchingAssignment.Currency;

                WageSystemService.Instance?.ComputeAndPayShiftWage(
                    worker, matchingAssignment, summary, scheduledShiftHours, hoursWorked, paymentCurrency);
            }

            _punchInTimeByWorker.Remove(worker);
            // Quest cleanup: drop the OnQuestPublished subscription AND abandon every
            // quest this building auto-claimed onto the worker during their shift.
            UnsubscribeWorkerQuestAutoClaim(worker);
            AbandonWorkerClaimedQuests(worker);
        }

        if (worker != null && IsWorkerOnShift(worker))
        {
            // Remove from the replicated roster (single source of truth).
            if (_activeWorkerIds != null && !string.IsNullOrEmpty(worker.CharacterId))
            {
                for (int i = _activeWorkerIds.Count - 1; i >= 0; i--)
                {
                    if (_activeWorkerIds[i].ToString() == worker.CharacterId)
                    {
                        _activeWorkerIds.RemoveAt(i);
                        break;
                    }
                }
            }

            Debug.Log($"<color=orange>[Building]</color> {worker.CharacterName} punched out of {buildingName}.");

            if (worker.CharacterJob != null)
            {
                var activeJobAssignment = worker.CharacterJob.ActiveJobs.FirstOrDefault(j => j.Workplace == this);
                if (activeJobAssignment != null && activeJobAssignment.AssignedJob != null)
                {
                    activeJobAssignment.AssignedJob.OnWorkerPunchOut();
                }
            }
        }
    }

    public virtual void AddToInventory(ItemInstance item)
    {
        if (item == null) return;
        // Idempotent on instance reference. Without this guard, an item that was
        // already absorbed into _inventory (via RefreshStorageInventory Pass 2 or
        // a prior AddToInventory) would double-count on every re-deposit:
        //   pickup → drop → FinishDropoff calls AddToInventory → same instance,
        //   already in list → list grows to 2 entries → GetItemCount returns 2.
        // The list intentionally tracks per-instance (not per-SO), so the dedupe
        // is on reference equality, which is what List<T>.Contains uses for class types.
        if (_inventory.Contains(item))
        {
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=#888888>[Building]</color> {item.ItemSO.ItemName} already in inventory of {buildingName} — skip duplicate add.");
            return;
        }
        _inventory.Add(item);
        MirrorInventoryAdd(item.ItemSO);
        // Inventory mutation can enable a previously-insufficient dispatch — wake the dispatcher.
        // See wiki/projects/optimisation-backlog.md entry #2 / B.
        if (LogisticsManager != null) LogisticsManager.MarkDispatchDirty();
        // Per-tick reachable from JobTransporter.NotifyDeliveryProgress / crafting completion / harvest deposit.
        // Gated to avoid the Windows console-buffer progressive-freeze documented in
        // wiki/gotchas/host-progressive-freeze-debug-log-spam.md.
        if (NPCDebug.VerboseJobs)
            Debug.Log($"<color=green>[Building]</color> {item.ItemSO.ItemName} added to inventory of {buildingName}.");
    }

    public virtual ItemInstance TakeFromInventory(ItemSO itemSO)
    {
        var item = _inventory.FirstOrDefault(i => i.ItemSO == itemSO);
        if (item != null)
        {
            _inventory.Remove(item);
            MirrorInventoryRemove(item.ItemSO);
            if (LogisticsManager != null) LogisticsManager.MarkDispatchDirty();
            return item;
        }
        return null;
    }

    public virtual bool RemoveExactItemFromInventory(ItemInstance exactItem)
    {
        if (exactItem != null && _inventory.Contains(exactItem))
        {
            _inventory.Remove(exactItem);
            MirrorInventoryRemove(exactItem.ItemSO);
            if (LogisticsManager != null) LogisticsManager.MarkDispatchDirty();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Read from the replicated <see cref="_inventoryItemIds"/> NetworkList so server AND clients
    /// return the same count. Falls back to the local <c>_inventory</c> on the server-only
    /// pre-OnNetworkSpawn window where the NetworkList isn't initialised yet.
    /// </summary>
    public virtual int GetItemCount(ItemSO itemSO)
    {
        if (itemSO == null) return 0;

        if (_inventoryItemIds != null && _inventoryItemIds.Count > 0)
        {
            var targetId = ResolveItemNetKey(itemSO);
            int count = 0;
            for (int i = 0; i < _inventoryItemIds.Count; i++)
            {
                if (_inventoryItemIds[i].Equals(targetId)) count++;
            }
            return count;
        }

        // Fallback: server logical inventory (also covers pre-network-spawn warm-up on the host).
        return _inventory.Count(i => i.ItemSO == itemSO);
    }

    /// <summary>
    /// Diagnostic toggle: flip true at runtime to trace inventory NetworkList writes/reads
    /// across server and client. Default off — when on, every server mirror write and every
    /// client display read logs once. Use to confirm whether replication or display is broken.
    /// </summary>
    public static bool DebugInventorySync = false;

    /// <summary>
    /// Resolve a stable network key for an ItemSO. Prefer the designer-set <see cref="ItemSO.ItemId"/>;
    /// fall back to the asset's <c>name</c> (always unique within a Resources folder) so empty IDs
    /// don't silently collapse every item into the same FixedString key. Without this fallback,
    /// every dropped item with a blank ItemId looks identical in the NetworkList — and
    /// <see cref="GetItemCount"/> for one ItemSO would falsely include every other empty-ID item,
    /// making the stock evaluator believe every shelf is already full.
    /// </summary>
    private static FixedString64Bytes ResolveItemNetKey(ItemSO itemSO)
    {
        if (itemSO == null) return new FixedString64Bytes(string.Empty);
        string id = itemSO.ItemId;
        if (string.IsNullOrEmpty(id))
        {
            id = itemSO.name; // asset filename — guaranteed non-empty for a real ScriptableObject asset
            Debug.LogWarning($"[CommercialBuilding] ItemSO '{itemSO.name}' has empty ItemId. Falling back to asset name as the network key — set ItemId in the inspector to silence this warning.");
        }
        return new FixedString64Bytes(id);
    }

    /// <summary>Server-only mirror helper: append an ItemSO ID to the replicated count list.</summary>
    private void MirrorInventoryAdd(ItemSO itemSO)
    {
        if (!IsServer || _inventoryItemIds == null || itemSO == null) return;
        _inventoryItemIds.Add(ResolveItemNetKey(itemSO));
        if (DebugInventorySync)
            Debug.Log($"<color=#88ff88>[InventorySync:Server-Add]</color> {buildingName}: +{itemSO.ItemId} (NetworkList now {_inventoryItemIds.Count} entries).");
    }

    // Cache of every ItemSO under Resources/Data/Item, lazily loaded once per session.
    // Lets <see cref="GetInventoryCountsByItemSO"/> resolve replicated ItemSO IDs back into
    // ItemSO references on the client (where the live ItemInstance objects don't exist).
    private static ItemSO[] _cachedAllItemSOs;

    /// <summary>
    /// Server- AND client-safe view of the storage inventory grouped by <see cref="ItemSO"/>,
    /// materialised from the replicated <see cref="_inventoryItemIds"/> NetworkList. Use this
    /// from any UI / display path that needs to render counts on a non-server peer — the
    /// raw <see cref="Inventory"/> list (full <c>ItemInstance</c> objects) is server-only and
    /// will return empty on clients.
    ///
    /// Allocates a fresh dictionary per call; safe for UI tick rates, avoid in tight loops.
    /// </summary>
    public Dictionary<ItemSO, int> GetInventoryCountsByItemSO()
    {
        var result = new Dictionary<ItemSO, int>();
        if (_inventoryItemIds == null || _inventoryItemIds.Count == 0) return result;

        if (_cachedAllItemSOs == null)
        {
            try
            {
                _cachedAllItemSOs = Resources.LoadAll<ItemSO>("Data/Item");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[CommercialBuilding] {buildingName}: GetInventoryCountsByItemSO failed to load ItemSOs from Resources/Data/Item.");
                return result;
            }
        }

        for (int i = 0; i < _inventoryItemIds.Count; i++)
        {
            string id = _inventoryItemIds[i].ToString();
            if (string.IsNullOrEmpty(id)) continue;

            // Match by ItemId first, fall back to asset name — mirror of ResolveItemNetKey
            // so items with empty ItemId still resolve back to the right ItemSO.
            ItemSO so = System.Array.Find(_cachedAllItemSOs, m => m != null && (m.ItemId == id || m.name == id));
            if (so == null) continue;

            if (result.TryGetValue(so, out int count)) result[so] = count + 1;
            else result[so] = 1;
        }
        return result;
    }

    /// <summary>
    /// Total number of items in the storage inventory (sum of replicated counts), safe to read
    /// on both server and client. Equivalent to summing <see cref="GetInventoryCountsByItemSO"/>
    /// values, but doesn't allocate a dictionary or do ItemSO resolution.
    /// </summary>
    public int InventoryTotalCount => _inventoryItemIds != null ? _inventoryItemIds.Count : _inventory.Count;

    /// <summary>Server-only mirror helper: remove the first matching ItemSO ID from the replicated count list.</summary>
    private void MirrorInventoryRemove(ItemSO itemSO)
    {
        if (!IsServer || _inventoryItemIds == null || itemSO == null) return;
        var targetId = ResolveItemNetKey(itemSO);
        for (int i = 0; i < _inventoryItemIds.Count; i++)
        {
            if (_inventoryItemIds[i].Equals(targetId))
            {
                _inventoryItemIds.RemoveAt(i);
                if (DebugInventorySync)
                    Debug.Log($"<color=#ff8888>[InventorySync:Server-Remove]</color> {buildingName}: -{itemSO.ItemId} (NetworkList now {_inventoryItemIds.Count} entries).");
                return;
            }
        }
    }

    // Reused buffer for the building's Physics.OverlapBoxNonAlloc calls
    // (StorageZone scan, BuildingZone scan, PickupZone scan). Shared across
    // call sites because all three are main-thread, non-reentrant. Pre-refactor
    // each call allocated a fresh Collider[] from Physics.OverlapBox (perf,
    // see wiki/projects/optimisation-backlog.md entry #2 / F).
    // Buffer size 128 is generous for typical storage zones; if a scan returns
    // exactly 128 we log a warning so the size can be bumped (defensive coding rule #31).
    private const int OverlapBufferSize = 128;
    private Collider[] _overlapBuffer;
    private Collider[] OverlapBuffer => _overlapBuffer ??= new Collider[OverlapBufferSize];

    /// <summary>
    /// Physically retrieves every WorldItem currently dropped inside the StorageZone.
    /// Used by employees (e.g. GatherStorageItems) to target the correct objects.
    /// </summary>
    /// <returns>A list of WorldItems located within the StorageZone's BoxCollider bounds.</returns>
    public virtual List<WorldItem> GetWorldItemsInStorage()
    {
        List<WorldItem> foundItems = new List<WorldItem>();

        if (_storageZone == null) return foundItems;

        BoxCollider boxCol = _storageZone.GetComponent<BoxCollider>();
        if (boxCol == null) return foundItems;

        Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
        Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

        var buffer = OverlapBuffer;
        int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, buffer, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
        if (hitCount == OverlapBufferSize)
        {
            Debug.LogWarning($"[CommercialBuilding] {buildingName}: GetWorldItemsInStorage saturated the OverlapBox buffer ({OverlapBufferSize}). Bump OverlapBufferSize — items beyond #{OverlapBufferSize} were truncated this scan.", this);
        }

        for (int i = 0; i < hitCount; i++)
        {
            var col = buffer[i];
            if (col == null) continue;
            // Chercher le composant sur l'objet ou sur son parent
            WorldItem worldItem = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
            if (worldItem != null && !foundItems.Contains(worldItem))
            {
                foundItems.Add(worldItem);
            }
        }

        return foundItems;
    }

    // ── StorageFurniture cache (perf, see wiki/projects/optimisation-backlog.md entry #2 / D).
    // Both FindStorageFurnitureForItem and GetItemsInStorageFurniture used to walk
    // GetFurnitureOfType<StorageFurniture>() (recursive across MainRoom + every SubRoom,
    // `is StorageFurniture` cast per furniture) on every call — fired from 5 GOAP actions
    // and `RefreshStorageInventory`, hundreds of times per second under the audited mix.
    // We cache the StorageFurniture set with a short TTL; manual invalidation hook covers
    // bursty changes (default-furniture spawn completion, player furniture place/pickup).
    protected const float FurnitureCacheTTLSeconds = 2f;
    private List<StorageFurniture> _cachedStorageFurniture;
    private float _storageFurnitureCacheValidUntil = -1f;

    /// <summary>
    /// Returns the cached list of <see cref="StorageFurniture"/> in this building.
    /// Refreshed lazily on TTL expiry or after <see cref="InvalidateStorageFurnitureCache"/>.
    /// The returned list is a SHARED reference — callers must treat it as read-only.
    /// </summary>
    private List<StorageFurniture> GetStorageFurnitureCached()
    {
        if (_cachedStorageFurniture == null)
        {
            _cachedStorageFurniture = new List<StorageFurniture>();
        }

        if (Time.time < _storageFurnitureCacheValidUntil)
        {
            return _cachedStorageFurniture;
        }

        _cachedStorageFurniture.Clear();
        foreach (var furniture in GetFurnitureOfType<StorageFurniture>())
        {
            if (furniture == null) continue;
            _cachedStorageFurniture.Add(furniture);
        }

        _storageFurnitureCacheValidUntil = Time.time + FurnitureCacheTTLSeconds;
        return _cachedStorageFurniture;
    }

    /// <summary>
    /// Force the next StorageFurniture lookup to re-walk rooms. Call after a known
    /// state change that should be reflected immediately — default-furniture spawn
    /// completion, player furniture place/pickup, etc.
    /// </summary>
    public void InvalidateStorageFurnitureCache()
    {
        _storageFurnitureCacheValidUntil = -1f;
    }

    /// <summary>
    /// Walks every <see cref="StorageFurniture"/> in this building (across all sub-rooms)
    /// and returns the first one that is unlocked and has a free slot compatible with
    /// <paramref name="item"/>. Used by the logistics cycle to prefer slot-based storage
    /// over the loose <see cref="StorageZone"/> drop.
    ///
    /// Selection is first-fit by furniture order in <see cref="ComplexRoom.GetFurnitureOfType{T}"/>;
    /// type-affinity (a wardrobe with only <c>WearableSlot</c>s rejecting a sword)
    /// falls out for free because <see cref="StorageFurniture.HasFreeSpaceForItem"/>
    /// already inspects per-slot <c>CanAcceptItem</c>.
    ///
    /// Returns <c>null</c> when no compatible furniture exists — caller should fall back
    /// to the legacy zone-drop behavior.
    /// </summary>
    public StorageFurniture FindStorageFurnitureForItem(ItemInstance item)
    {
        if (item == null) return null;
        var cached = GetStorageFurnitureCached();
        for (int i = 0; i < cached.Count; i++)
        {
            var furniture = cached[i];
            if (furniture == null || furniture.IsLocked) continue;
            if (furniture.HasFreeSpaceForItem(item)) return furniture;
        }
        return null;
    }

    /// <summary>
    /// Returns every <see cref="ItemInstance"/> currently held in a <see cref="StorageFurniture"/>
    /// slot inside this building, paired with the owning furniture so the caller can
    /// queue a <c>CharacterTakeFromFurnitureAction</c>. Used by
    /// <c>GoapAction_StageItemForPickup</c> to find reserved instances that live in
    /// slots rather than as loose <see cref="WorldItem"/>s.
    /// </summary>
    public IEnumerable<(StorageFurniture furniture, ItemInstance item)> GetItemsInStorageFurniture()
    {
        var cached = GetStorageFurnitureCached();
        for (int i = 0; i < cached.Count; i++)
        {
            var furniture = cached[i];
            if (furniture == null) continue;
            var slots = furniture.ItemSlots;
            if (slots == null) continue;
            for (int j = 0; j < slots.Count; j++)
            {
                var slot = slots[j];
                if (slot != null && !slot.IsEmpty()) yield return (furniture, slot.ItemInstance);
            }
        }
    }

    /// <summary>
    /// Counts <paramref name="itemSO"/> units that physically exist at this building but are
    /// NOT yet visible in the logical <see cref="Inventory"/>. Covers two in-flight cases:
    ///
    /// 1. Loose WorldItems sitting inside <see cref="Building.BuildingZone"/> (e.g. just
    ///    spawned at a <c>CraftingStation._outputPoint</c>, waiting for
    ///    <c>GoapAction_GatherStorageItems</c> or <c>RefreshStorageInventory</c> Pass 2).
    /// 2. Items carried by this building's own assigned workers (the Logistics Manager
    ///    picking a crafted item up to move it to storage temporarily despawns the
    ///    WorldItem, so (1) alone would miss that case).
    ///
    /// Used by the dispatcher to distinguish "crafted but mid-transit to storage" from
    /// "actually stolen" when a completed <see cref="CraftingOrder"/> exists but no physical
    /// stock is visible in <see cref="StorageZone"/> yet. Returns 0 when there's no
    /// BuildingZone collider (legacy prefabs) AND no workers hold matching items, which
    /// degrades gracefully to the pre-fix behavior.
    /// </summary>
    public virtual int CountUnabsorbedItemsInBuildingZone(ItemSO itemSO)
    {
        if (itemSO == null) return 0;

        int count = 0;
        HashSet<ItemInstance> counted = new HashSet<ItemInstance>();

        // (1) Loose WorldItems inside BuildingZone.
        if (BuildingZone is BoxCollider boxCol)
        {
            Vector3 center = boxCol.transform.TransformPoint(boxCol.center);
            Vector3 halfExtents = Vector3.Scale(boxCol.size, boxCol.transform.lossyScale) * 0.5f;

            var buffer = OverlapBuffer;
            int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, buffer, boxCol.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
            if (hitCount == OverlapBufferSize)
            {
                Debug.LogWarning($"[CommercialBuilding] {buildingName}: CountUnabsorbedItemsInBuildingZone saturated the OverlapBox buffer ({OverlapBufferSize}). Bump OverlapBufferSize — items beyond #{OverlapBufferSize} were truncated this scan.", this);
            }

            for (int i = 0; i < hitCount; i++)
            {
                var col = buffer[i];
                if (col == null) continue;
                WorldItem wi = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
                if (wi == null || wi.ItemInstance == null) continue;
                if (wi.IsBeingCarried) continue;
                if (wi.ItemInstance.ItemSO != itemSO) continue;
                if (_inventory.Contains(wi.ItemInstance)) continue; // already visible in the dispatcher's view
                if (!counted.Add(wi.ItemInstance)) continue;
                count++;
            }
        }

        // (2) Items held by this building's own workers. Covers the window where the
        // Logistics Manager has picked a crafted WorldItem up (despawning it from the
        // scene) but hasn't yet dropped it in storage via FinishDropoff → AddToInventory.
        for (int i = 0; i < _jobs.Count; i++)
        {
            Character worker = _jobs[i]?.Worker;
            if (worker == null) continue;

            try
            {
                var equipment = worker.CharacterEquipment;
                if (equipment != null && equipment.HaveInventory())
                {
                    var slots = equipment.GetInventory()?.ItemSlots;
                    if (slots != null)
                    {
                        for (int s = 0; s < slots.Count; s++)
                        {
                            var slot = slots[s];
                            if (slot == null || slot.IsEmpty()) continue;
                            var inst = slot.ItemInstance;
                            if (inst == null || inst.ItemSO != itemSO) continue;
                            if (_inventory.Contains(inst)) continue;
                            if (!counted.Add(inst)) continue;
                            count++;
                        }
                    }
                }

                var hands = worker.CharacterVisual?.BodyPartsController?.HandsController;
                if (hands != null && hands.CarriedItem != null && hands.CarriedItem.ItemSO == itemSO)
                {
                    var carried = hands.CarriedItem;
                    if (!_inventory.Contains(carried) && counted.Add(carried)) count++;
                }
            }
            catch (System.Exception e)
            {
                // Defensive: a broken equipment component on one worker must not break the whole stock check.
                Debug.LogException(e);
                Debug.LogError($"[CommercialBuilding] {buildingName}: CountUnabsorbedItemsInBuildingZone threw while scanning worker '{worker?.CharacterName}'. Skipping that worker's contribution this tick.");
            }
        }

        return count;
    }

    /// <summary>
    /// Two-way sync between the logical inventory (<see cref="_inventory"/>) and the physical
    /// WorldItems actually sitting in the <see cref="StorageZone"/>.
    ///
    /// Pass 1 — remove ghosts: logical entries with no physical counterpart (fell through the
    /// map, despawned, stolen, etc.). Any ghost referenced by a live TransportOrder is also
    /// reported so the order can be recomputed.
    ///
    /// Pass 2 — absorb orphans: physical WorldItems inside StorageZone bounds that were never
    /// registered logically. This happens whenever an item reaches the zone via a path that
    /// doesn't call <see cref="AddToInventory"/> — harvesters dropping in a DepositZone that
    /// overlaps StorageZone, couriers dropping directly into the zone, player drops, etc.
    /// Without this, GetItemCount stays at 0 forever and the logistics stock check re-orders
    /// wood that physically exists in the yard.
    /// </summary>
    public virtual void RefreshStorageInventory()
    {
        List<WorldItem> physicalItems = GetWorldItemsInStorage();

        // Phase-A: PickupZone contents are outbound staging — still "ours" logically, but not
        // in StorageZone. Merge them into the physical set for Pass-1 presence checks so a
        // staged instance isn't flagged as a ghost. Pass-2 absorption intentionally does NOT
        // include them (they're already reserved by a TransportOrder and will leave with the
        // transporter, so re-absorbing would re-register them in _inventory needlessly —
        // _inventory.Contains(...) already protects correctly-tracked instances anyway).
        if (_pickupZone != null)
        {
            BoxCollider pickupBox = _pickupZone.GetComponent<BoxCollider>();
            if (pickupBox != null)
            {
                Vector3 center = pickupBox.transform.TransformPoint(pickupBox.center);
                Vector3 halfExtents = Vector3.Scale(pickupBox.size, pickupBox.transform.lossyScale) * 0.5f;
                try
                {
                    var buffer = OverlapBuffer;
                    int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, buffer, pickupBox.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
                    if (hitCount == OverlapBufferSize)
                    {
                        Debug.LogWarning($"[CommercialBuilding] {buildingName}: RefreshStorageInventory PickupZone scan saturated the OverlapBox buffer ({OverlapBufferSize}). Bump OverlapBufferSize — items beyond #{OverlapBufferSize} were truncated this scan.", this);
                    }
                    for (int i = 0; i < hitCount; i++)
                    {
                        var col = buffer[i];
                        if (col == null) continue;
                        WorldItem wi = col.GetComponent<WorldItem>() ?? col.GetComponentInParent<WorldItem>();
                        if (wi != null && !physicalItems.Contains(wi)) physicalItems.Add(wi);
                    }
                }
                catch (System.Exception e)
                {
                    // Defensive (rule #31): a PhysX hiccup on PickupZone scan must not derail the whole refresh.
                    Debug.LogException(e);
                    Debug.LogError($"[CommercialBuilding] {buildingName}: Physics.OverlapBox threw while scanning PickupZone. Pass 1 proceeds with StorageZone items only.");
                }
            }
        }

        List<ItemInstance> ghostlyInstances = new List<ItemInstance>();

        // Collect instances actively reserved by live TransportOrders. These must NEVER be
        // ghosted here: with non-kinematic WorldItem physics (post-FreezeOnGround removal),
        // a settling item can be momentarily missed by Physics.OverlapBox while still being
        // perfectly valid. Ghosting a reserved instance cascades into ReportMissingReservedItem
        // and kills an in-flight transport. True missing-item detection happens where it
        // belongs — when the transporter arrives at the source and the pickup resolves.
        HashSet<ItemInstance> reservedInstances = null;
        if (LogisticsManager != null)
        {
            reservedInstances = new HashSet<ItemInstance>();
            foreach (var transport in LogisticsManager.PlacedTransportOrders)
            {
                if (transport == null || transport.ReservedItems == null) continue;
                foreach (var r in transport.ReservedItems) reservedInstances.Add(r);
            }
        }

        // Items currently held in StorageFurniture slots have NO matching WorldItem
        // (they live as logical-only data inside the furniture's _itemSlots — the
        // CharacterStoreInFurnitureAction path never spawns a WorldItem). Without this
        // set, Pass 1 would ghost every furniture-stored instance on the next punch-in,
        // silently undoing every deposit done by the logistics-cycle furniture path.
        HashSet<ItemInstance> furnitureStoredInstances = new HashSet<ItemInstance>();
        foreach (var (_, slotItem) in GetItemsInStorageFurniture())
        {
            if (slotItem != null) furnitureStoredInstances.Add(slotItem);
        }

        foreach (var logicalInstance in _inventory)
        {
            if (reservedInstances != null && reservedInstances.Contains(logicalInstance)) continue;
            if (furnitureStoredInstances.Contains(logicalInstance)) continue;

            bool isPhysicallyPresent = false;

            // Check whether a physical WorldItem matches this logical instance.
            foreach (var worldItem in physicalItems)
            {
                if (worldItem.ItemInstance == logicalInstance)
                {
                    isPhysicallyPresent = true;
                    break;
                }
            }

            // If it is not on the ground AND it is not being carried by anyone, it is a ghost.
            if (!isPhysicallyPresent)
            {
                // Extra check: does it actually have an assigned carrier?
                // WorldItem.IsBeingCarried tracks effective carrying.
                // In our architecture, if an ItemInstance has no current owner and is lost, we remove it.
                ghostlyInstances.Add(logicalInstance);
            }
        }

        if (ghostlyInstances.Count > 0)
        {
            Debug.LogWarning($"<color=orange>[CommercialBuilding]</color> {buildingName}: Audit detected {ghostlyInstances.Count} logical objects with no physical counterpart! Cleaning up...");

            foreach (var ghost in ghostlyInstances)
            {
                _inventory.Remove(ghost);
                MirrorInventoryRemove(ghost.ItemSO);

                // If this ghost item was reserved by a logistics manager for an order (Transport/Buy), report it.
                if (LogisticsManager != null)
                {
                    // Find which order had reserved this ghost item.
                    var brokenTransportOrder = LogisticsManager.PlacedTransportOrders.FirstOrDefault(t => t.ReservedItems.Contains(ghost));
                    if (brokenTransportOrder != null)
                    {
                        LogisticsManager.ReportMissingReservedItem(brokenTransportOrder);
                    }
                }
            }
        }

        // Pass 2 — absorb orphans: physical items in storage that aren't tracked logically.
        int absorbed = 0;
        foreach (var worldItem in physicalItems)
        {
            if (worldItem == null || worldItem.ItemInstance == null) continue;
            if (worldItem.IsBeingCarried) continue;
            if (_inventory.Contains(worldItem.ItemInstance)) continue;

            _inventory.Add(worldItem.ItemInstance);
            MirrorInventoryAdd(worldItem.ItemInstance.ItemSO);
            absorbed++;
        }

        if (absorbed > 0)
        {
            Debug.Log($"<color=green>[CommercialBuilding]</color> {buildingName}: Audit absorbed {absorbed} orphan physical object(s) into the logical inventory.");
        }
    }

    public virtual bool HasRequiredIngredients(IEnumerable<CraftingIngredient> ingredients, int multiplier = 1)
    {
        foreach (var ingredient in ingredients)
        {
            if (GetItemCount(ingredient.Item) < ingredient.Amount * multiplier)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Indicates whether this building produces or supplies the requested item.
    /// Subclasses must override this to expose what they offer.
    /// </summary>
    public virtual bool ProducesItem(ItemSO item)
    {
        return false;
    }

    /// <summary>
    /// Indicates whether producing this item requires a crafting order (CraftingOrder).
    /// If false, a transporter can be sent directly (TransportOrder) to fetch the stock (e.g. Harvester).
    /// </summary>
    public virtual bool RequiresCraftingFor(ItemSO item)
    {
        return false;
    }

    /// <summary>
    /// V2 Logistics hook for dynamic virtual stock generation (like HarvestingBuildings).
    /// </summary>
    public virtual bool TryFulfillOrder(BuyOrder order, int amount)
    {
        return false;
    }

    /// <summary>
    /// Compute the end-of-shift time as a 0..1 fraction of the day, by finding the
    /// latest endHour across all of this assignment's work-schedule entries.
    /// Returns 1.0 (end-of-day) if no entries are present (defensive default).
    /// </summary>
    private float ComputeScheduledEndTime01(JobAssignment assignment)
    {
        if (assignment == null || assignment.WorkScheduleEntries == null || assignment.WorkScheduleEntries.Count == 0)
            return 1f;
        int latestEndHour = 0;
        for (int i = 0; i < assignment.WorkScheduleEntries.Count; i++)
        {
            var entry = assignment.WorkScheduleEntries[i];
            if (entry.endHour > latestEndHour) latestEndHour = entry.endHour;
        }
        return Mathf.Clamp01(latestEndHour / 24f);
    }

    /// <summary>
    /// Stable per-building id used as the WorkPlaceRecord key. Backed by
    /// <see cref="Building.BuildingId"/> — a GUID assigned at network spawn time and
    /// persisted in CommunityData / save files. Renaming the GameObject at runtime no
    /// longer forks WorkPlaceRecord history.
    /// </summary>
    private string GetBuildingIdForWorklog()
    {
        return BuildingId;
    }

    /// <summary>
    /// Human-readable label snapshot-stored ONCE in
    /// <c>WorkPlaceRecord.BuildingDisplayName</c> at the worker's first punch-in.
    /// Backed by <see cref="Building.BuildingDisplayName"/>.
    /// </summary>
    private string GetBuildingDisplayNameForWorklog()
    {
        return BuildingDisplayName;
    }

    /// <summary>
    /// Sum of (endHour - startHour) across the assignment's WorkScheduleEntries.
    /// Returns 8f as a defensive default if no entries are present.
    /// </summary>
    private float ComputeScheduledShiftHours(JobAssignment assignment)
    {
        if (assignment == null || assignment.WorkScheduleEntries == null || assignment.WorkScheduleEntries.Count == 0)
            return 8f;
        int totalHours = 0;
        for (int i = 0; i < assignment.WorkScheduleEntries.Count; i++)
        {
            var entry = assignment.WorkScheduleEntries[i];
            int duration = entry.endHour - entry.startHour;
            if (duration > 0) totalHours += duration;
        }
        return Mathf.Max(0.0001f, totalHours);
    }

    /// <summary>
    /// (now - punchInTime) in hours, capped at scheduled-end-of-shift (no overtime credit
    /// for the minimum wage component — handled also by WageCalculator clamp01, but capping
    /// here keeps the hours-worked field truthful for diagnostics).
    /// </summary>
    private float ComputeHoursWorked(Character worker)
    {
        if (!_punchInTimeByWorker.TryGetValue(worker, out float punchInHours)) return 0f;
        float nowHours = MWI.Time.TimeManager.Instance != null
            ? MWI.Time.TimeManager.Instance.CurrentTime01 * 24f
            : punchInHours;
        return Mathf.Max(0f, nowHours - punchInHours);
    }
}
