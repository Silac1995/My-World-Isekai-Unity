# Quest System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a unified Quest primitive consumed by both players and NPCs (Hybrid C unification), with a player-facing HUD (tracker + log + world markers) and Job-producer integration via existing `BuildingTaskManager` and `BuildingLogisticsManager`.

**Architecture:** Existing `BuildingTask`, `BuyOrder`, `TransportOrder`, `CraftingOrder` types implement the new `IQuest` interface directly (no adapter wrappers). New `CharacterQuestLog` subsystem on every Character holds claimed quests + denormalized snapshots, syncs via `NetworkList<FixedString64Bytes>` + targeted `[ClientRpc]` for snapshot push, persists via `ICharacterSaveData<QuestLogSaveData>`. Player HUD subscribes to local-player CharacterQuestLog only; world markers filter by `OriginMapId == localPlayer.CharacterMapTracker.CurrentMapID`.

**Tech Stack:** Unity 6, Netcode for GameObjects, Newtonsoft.Json (existing save pipeline), NUnit (EditMode tests where pure-logic helpers warrant).

**Spec:** `docs/superpowers/specs/2026-04-23-quest-system-design.md`.

---

## File Structure

### NEW files

```
Assets/Scripts/Quest/
  IQuest.cs                        # interface + QuestType + QuestState enums
  IQuestTarget.cs                  # target interface
  Targets/
    HarvestableTarget.cs           # wraps Harvestable
    WorldItemTarget.cs             # wraps WorldItem
    ZoneTarget.cs                  # wraps Zone (region + center waypoint)
    BuildingTarget.cs              # wraps Building (movement target)
    CharacterTarget.cs             # wraps Character (object marker)

Assets/Scripts/Character/CharacterQuestLog/
  CharacterQuestLog.cs             # subsystem (NetworkBehaviour + ICharacterSaveData)
  QuestLogSaveData.cs              # outer DTO
  QuestSnapshotEntry.cs            # per-quest snapshot for save + late-joiner

Assets/Scripts/UI/Quest/
  UI_QuestTracker.cs               # always-visible top-right widget script
  UI_QuestLogWindow.cs             # full panel script (extends UI_WindowBase)
  QuestWorldMarkerRenderer.cs      # spawns/despawns marker prefabs per quest

Assets/Prefabs/UI/Quest/
  UI_QuestTracker.prefab           # tracker widget
  UI_QuestLogWindow.prefab         # log window
  QuestMarker_Diamond.prefab       # floating gold marker (object/action targets)
  QuestMarker_Beacon.prefab        # vertical light shaft + ring (movement targets)
  QuestZone_Fill.prefab            # flat semi-transparent gold + perimeter line
```

### MODIFIED files

```
Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs              # implement IQuest
Assets/Scripts/World/Buildings/Tasks/HarvestResourceTask.cs       # IQuest specifics
Assets/Scripts/World/Buildings/Tasks/PickupLooseItemTask.cs       # IQuest specifics
Assets/Scripts/World/Jobs/BuyOrder.cs                              # implement IQuest
Assets/Scripts/World/Jobs/TransportOrder.cs                        # implement IQuest
Assets/Scripts/World/Jobs/CraftingOrder.cs                         # implement IQuest
Assets/Scripts/World/Buildings/BuildingTaskManager.cs              # add events
Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs     # add events
Assets/Scripts/World/Buildings/CommercialBuilding.cs               # aggregator + auto-claim hook
Assets/Scripts/Character/Character.cs                              # facade exposes CharacterQuestLog
Assets/Scripts/UI/PlayerUI.cs                                      # register quest UI for local player
Assets/Scripts/World/Zones/Zone.cs                                 # add public Bounds property
Assets/Scripts/Interactable/Harvestable.cs                         # add public RemainingYield property
Assets/Prefabs/Character/Character_Default.prefab                  # add CharacterQuestLog child GO
Assets/Scenes/GameScene.unity                                      # add UI_QuestTracker + UI_QuestLogWindow under PlayerUI
.agent/skills/quest-system/SKILL.md                                # NEW
.agent/skills/job_system/SKILL.md                                  # cross-link
.agent/skills/logistics_cycle/SKILL.md                             # cross-link
.agent/skills/save-load-system/SKILL.md                            # CharacterQuestLog persistence
.agent/skills/player_ui/SKILL.md                                   # quest HUD widgets
wiki/systems/quest-system.md                                       # NEW
wiki/systems/jobs-and-logistics.md                                 # change log
wiki/systems/commercial-building.md                                # change log
wiki/systems/building-logistics-manager.md                         # change log
wiki/systems/building-task-manager.md                              # change log
wiki/systems/worker-wages-and-performance.md                       # cross-link
.claude/agents/building-furniture-specialist.md                    # awareness
.claude/agents/npc-ai-specialist.md                                # awareness
```

---

## Phase 1 — Core Interfaces

### Task 1: Create QuestType + QuestState enums + IQuest interface

**Files:**
- Create: `Assets/Scripts/Quest/IQuest.cs`

- [ ] **Step 1: Create the interface file**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Quests
{
    public enum QuestType
    {
        Job = 0,
        Main = 1,
        Bounty = 2,
        Event = 3,
        Custom = 99
    }

    public enum QuestState
    {
        Open = 0,
        Full = 1,
        Completed = 2,
        Abandoned = 3,
        Expired = 4
    }

    /// <summary>
    /// Unified work-instruction primitive consumed by both players and NPCs.
    /// Existing types (BuildingTask, BuyOrder, TransportOrder, CraftingOrder)
    /// implement this directly. NPC GOAP keeps its existing typed APIs unchanged.
    /// </summary>
    public interface IQuest
    {
        // Identity & origin
        string QuestId { get; }
        string OriginWorldId { get; }
        string OriginMapId { get; }
        Character Issuer { get; }
        QuestType Type { get; }

        // Display data
        string Title { get; }
        string InstructionLine { get; }
        string Description { get; }

        // Lifecycle
        QuestState State { get; }
        bool IsExpired { get; }
        int RemainingDays { get; }

        // Progress
        int TotalProgress { get; }
        int Required { get; }
        int MaxContributors { get; }

        // Contributors
        IReadOnlyList<Character> Contributors { get; }
        IReadOnlyDictionary<string, int> Contribution { get; }

        // Mutations (server-authoritative — caller must be on server)
        bool TryJoin(Character character);
        bool TryLeave(Character character);
        void RecordProgress(Character character, int amount);

        // Targeting
        IQuestTarget Target { get; }

        // Events
        event Action<IQuest> OnStateChanged;
        event Action<IQuest, Character, int> OnProgressRecorded;
    }
}
```

- [ ] **Step 2: Verify compile**

Run `mcp__ai-game-developer__assets-refresh` then `mcp__ai-game-developer__console-get-logs`. Zero NEW compile errors expected (`IQuestTarget` is referenced but doesn't exist yet — this will fail compile until Task 2 lands).

If compile fails on `IQuestTarget`, that's expected — proceed to commit anyway. The next task fixes it.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Quest/IQuest.cs Assets/Scripts/Quest/IQuest.cs.meta
git commit -m "feat(quests): add IQuest interface + QuestType/QuestState enums

NOTE: References IQuestTarget which lands in Task 2 — compile fails
until then."
```

### Task 2: Create IQuestTarget interface

**Files:**
- Create: `Assets/Scripts/Quest/IQuestTarget.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

namespace MWI.Quests
{
    /// <summary>
    /// What a quest points at in the world. Pluggable so a single renderer
    /// handles Harvestables, WorldItems, Zones, Buildings, Characters via one path.
    /// </summary>
    public interface IQuestTarget
    {
        /// <summary>World position used by the floating-marker renderer (style A).</summary>
        Vector3 GetWorldPosition();

        /// <summary>Non-null = "go here" target (style B beacon). Null = "interact with this object" target (style A diamond).</summary>
        Vector3? GetMovementTarget();

        /// <summary>Non-null = region target (zone fill renders). Null = point target.</summary>
        Bounds? GetZoneBounds();

        /// <summary>Display name for HUD text ("the East Woods", "Bob's Smithy").</summary>
        string GetDisplayName();

        /// <summary>v1: always returns true. Stub for future fog-of-war / hidden quests.</summary>
        bool IsVisibleToPlayer(Character viewer);
    }
}
```

- [ ] **Step 2: Verify compile**

`mcp__ai-game-developer__assets-refresh` + `mcp__ai-game-developer__console-get-logs`. **Now zero NEW errors** — both interfaces resolve.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Quest/IQuestTarget.cs Assets/Scripts/Quest/IQuestTarget.cs.meta
git commit -m "feat(quests): add IQuestTarget interface (resolves Task 1 compile error)"
```

---

## Phase 2 — Target Wrappers (5 concrete IQuestTargets)

### Task 3: HarvestableTarget

**Files:**
- Create: `Assets/Scripts/Quest/Targets/HarvestableTarget.cs`
- Modify: `Assets/Scripts/Interactable/Harvestable.cs` — add `RemainingYield` property

- [ ] **Step 1: Add RemainingYield to Harvestable**

Open `Assets/Scripts/Interactable/Harvestable.cs`. Find the `_currentHarvestCount` and `_maxHarvestCount` fields (around lines 18-22). Add a public property near them:

```csharp
/// <summary>
/// Remaining yield this harvestable can produce. int.MaxValue if non-depletable.
/// </summary>
public int RemainingYield => _isDepletable
    ? Mathf.Max(0, _maxHarvestCount - _currentHarvestCount)
    : int.MaxValue;
```

If `_isDepletable` doesn't exist as a bool field on Harvestable, replace with the actual depletion-tracking field (read the file to confirm). If unsure, fall back to: `public int RemainingYield => Mathf.Max(0, _maxHarvestCount - _currentHarvestCount);`.

- [ ] **Step 2: Create HarvestableTarget.cs**

```csharp
using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a single Harvestable (tree, ore vein, berry bush).</summary>
    public class HarvestableTarget : IQuestTarget
    {
        private readonly Harvestable _harvestable;

        public HarvestableTarget(Harvestable harvestable) { _harvestable = harvestable; }

        public Vector3 GetWorldPosition() => _harvestable != null ? _harvestable.transform.position : Vector3.zero;
        public Vector3? GetMovementTarget() => null;  // object target, not movement
        public Bounds? GetZoneBounds() => null;
        public string GetDisplayName() => _harvestable != null ? _harvestable.name : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;  // v1
    }
}
```

- [ ] **Step 3: Verify compile**

`assets-refresh` + `console-get-logs`. Zero new errors.

- [ ] **Step 4: Commit (include all 4 .meta files: 2 .cs + Targets folder + parent Quest folder if new)**

```bash
git add Assets/Scripts/Quest/ Assets/Scripts/Quest.meta Assets/Scripts/Interactable/Harvestable.cs
git commit -m "feat(quests): add HarvestableTarget + Harvestable.RemainingYield property"
```

Verify with `git show --stat <SHA>` — expect 5+ files (Targets folder meta, Quest folder meta, HarvestableTarget.cs + .meta, Harvestable.cs).

### Task 4: WorldItemTarget

**Files:**
- Create: `Assets/Scripts/Quest/Targets/WorldItemTarget.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a single WorldItem (loose item to pick up).</summary>
    public class WorldItemTarget : IQuestTarget
    {
        private readonly WorldItem _worldItem;

        public WorldItemTarget(WorldItem worldItem) { _worldItem = worldItem; }

        public Vector3 GetWorldPosition() => _worldItem != null ? _worldItem.transform.position : Vector3.zero;
        public Vector3? GetMovementTarget() => null;
        public Bounds? GetZoneBounds() => null;
        public string GetDisplayName() => _worldItem != null && _worldItem.ItemInstance != null
            ? _worldItem.ItemInstance.ItemSO.DisplayName
            : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
```

If `WorldItem.ItemInstance.ItemSO.DisplayName` doesn't exist, replace `DisplayName` with `name` or whichever property the project uses (read `ItemSO.cs` to confirm — likely `displayName` or `Name`).

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/Quest/Targets/WorldItemTarget.cs Assets/Scripts/Quest/Targets/WorldItemTarget.cs.meta
git commit -m "feat(quests): add WorldItemTarget"
```

### Task 5: ZoneTarget (+ add public Bounds to Zone)

**Files:**
- Create: `Assets/Scripts/Quest/Targets/ZoneTarget.cs`
- Modify: `Assets/Scripts/World/Zones/Zone.cs` — add public `Bounds` property

- [ ] **Step 1: Add Bounds property to Zone.cs**

Open `Assets/Scripts/World/Zones/Zone.cs`. After the `_boxCollider` field (around line 20), add:

```csharp
/// <summary>World-space bounds of this zone's collider. Returns empty Bounds if no collider.</summary>
public Bounds Bounds => _boxCollider != null ? _boxCollider.bounds : new Bounds();
```

- [ ] **Step 2: Create ZoneTarget.cs**

```csharp
using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a Zone — region target with optional center waypoint.</summary>
    public class ZoneTarget : IQuestTarget
    {
        private readonly Zone _zone;
        private readonly string _displayName;

        public ZoneTarget(Zone zone, string displayName)
        {
            _zone = zone;
            _displayName = displayName;
        }

        public Vector3 GetWorldPosition() => _zone != null ? _zone.Bounds.center : Vector3.zero;
        public Vector3? GetMovementTarget() => null;     // zone-fill renders; no separate beacon
        public Bounds? GetZoneBounds() => _zone != null ? _zone.Bounds : (Bounds?)null;
        public string GetDisplayName() => _displayName ?? "<unnamed zone>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
```

- [ ] **Step 3: Compile + commit**

```bash
git add Assets/Scripts/Quest/Targets/ZoneTarget.cs Assets/Scripts/Quest/Targets/ZoneTarget.cs.meta Assets/Scripts/World/Zones/Zone.cs
git commit -m "feat(quests): add ZoneTarget + public Zone.Bounds property"
```

### Task 6: BuildingTarget

**Files:**
- Create: `Assets/Scripts/Quest/Targets/BuildingTarget.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

namespace MWI.Quests
{
    /// <summary>
    /// Quest target wrapping a Building. Movement target — uses the building's
    /// DeliveryZone center for "go here" beacon rendering.
    /// </summary>
    public class BuildingTarget : IQuestTarget
    {
        private readonly Building _building;

        public BuildingTarget(Building building) { _building = building; }

        public Vector3 GetWorldPosition() =>
            _building != null ? _building.transform.position : Vector3.zero;

        public Vector3? GetMovementTarget()
        {
            if (_building == null) return null;
            // Prefer DeliveryZone if commercial; fall back to building origin.
            if (_building is CommercialBuilding cb && cb.DeliveryZone != null)
                return cb.DeliveryZone.Bounds.center;
            return _building.transform.position;
        }

        public Bounds? GetZoneBounds() => null;  // no zone fill for building targets
        public string GetDisplayName() => _building != null ? _building.BuildingDisplayName : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
```

If `CommercialBuilding.DeliveryZone` is private or differently named, adjust accordingly (the wage system uses it; check `CommercialBuilding.cs` Zone fields).

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/Quest/Targets/BuildingTarget.cs Assets/Scripts/Quest/Targets/BuildingTarget.cs.meta
git commit -m "feat(quests): add BuildingTarget (movement target via DeliveryZone)"
```

### Task 7: CharacterTarget

**Files:**
- Create: `Assets/Scripts/Quest/Targets/CharacterTarget.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

namespace MWI.Quests
{
    /// <summary>Quest target wrapping a Character (NPC to talk to / deliver to).</summary>
    public class CharacterTarget : IQuestTarget
    {
        private readonly Character _character;

        public CharacterTarget(Character character) { _character = character; }

        public Vector3 GetWorldPosition() =>
            _character != null ? _character.transform.position : Vector3.zero;
        public Vector3? GetMovementTarget() => null;  // diamond marker over head, not beacon
        public Bounds? GetZoneBounds() => null;
        public string GetDisplayName() =>
            _character != null ? _character.CharacterName : "<destroyed>";
        public bool IsVisibleToPlayer(Character viewer) => true;
    }
}
```

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/Quest/Targets/CharacterTarget.cs Assets/Scripts/Quest/Targets/CharacterTarget.cs.meta
git commit -m "feat(quests): add CharacterTarget (object marker over NPC head)"
```

---

## Phase 3 — Existing Primitives Implement IQuest

### Task 8: BuildingTask implements IQuest

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs`

- [ ] **Step 1: Read the existing class**

Read `Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs` in full. Confirm the existing fields: `Target`, `MaxWorkers`, `ClaimedByWorkers`, `IsClaimed`, `IsValid()`, `CanBeClaimed()`, `Claim(worker)`, `Unclaim(worker)`.

- [ ] **Step 2: Add IQuest implementation**

Replace the class declaration with:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using MWI.Quests;

public abstract class BuildingTask : IQuest
{
    public MonoBehaviour Target { get; protected set; }
    public List<Character> ClaimedByWorkers { get; private set; } = new List<Character>();
    public bool IsClaimed => ClaimedByWorkers.Count > 0;
    public virtual int MaxWorkers => 1;

    // === IQuest implementation ===

    public string QuestId { get; private set; } = System.Guid.NewGuid().ToString("N");
    public string OriginWorldId { get; protected set; } = string.Empty;
    public string OriginMapId { get; protected set; } = string.Empty;
    public Character Issuer { get; protected set; }
    public virtual MWI.Quests.QuestType Type => MWI.Quests.QuestType.Job;

    public virtual string Title => GetType().Name.Replace("Task", "");
    public virtual string InstructionLine => Title;
    public virtual string Description => Title;

    public MWI.Quests.QuestState State
    {
        get
        {
            if (!IsValid()) return MWI.Quests.QuestState.Expired;
            if (IsCompletedInternal()) return MWI.Quests.QuestState.Completed;
            if (ClaimedByWorkers.Count >= MaxWorkers) return MWI.Quests.QuestState.Full;
            return MWI.Quests.QuestState.Open;
        }
    }
    public bool IsExpired => !IsValid();
    public virtual int RemainingDays => int.MaxValue;  // BuildingTasks don't expire by days

    public virtual int TotalProgress
    {
        get { int sum = 0; foreach (var v in _contribution.Values) sum += v; return sum; }
    }
    public virtual int Required => 1;
    public int MaxContributors => MaxWorkers;

    public IReadOnlyList<Character> Contributors => ClaimedByWorkers;
    public IReadOnlyDictionary<string, int> Contribution => _contribution;
    private readonly Dictionary<string, int> _contribution = new Dictionary<string, int>();

    public IQuestTarget QuestTarget { get; protected set; }
    IQuestTarget IQuest.Target => QuestTarget;

    public event Action<IQuest> OnStateChanged;
    public event Action<IQuest, Character, int> OnProgressRecorded;

    public bool TryJoin(Character character)
    {
        if (character == null || !CanBeClaimed() || ClaimedByWorkers.Contains(character)) return false;
        Claim(character);
        OnStateChanged?.Invoke(this);
        return true;
    }

    public bool TryLeave(Character character)
    {
        if (character == null || !ClaimedByWorkers.Contains(character)) return false;
        Unclaim(character);
        OnStateChanged?.Invoke(this);
        return true;
    }

    public void RecordProgress(Character character, int amount)
    {
        if (character == null || amount <= 0) return;
        var id = character.CharacterId;
        if (string.IsNullOrEmpty(id)) return;
        _contribution.TryGetValue(id, out int prev);
        _contribution[id] = prev + amount;
        OnProgressRecorded?.Invoke(this, character, amount);
        if (IsCompletedInternal())
        {
            OnStateChanged?.Invoke(this);
        }
    }

    /// <summary>Subclasses override to define completion (default: TotalProgress >= Required).</summary>
    protected virtual bool IsCompletedInternal() => TotalProgress >= Required;

    // === Existing methods (unchanged) ===

    public abstract bool IsValid();
    public virtual bool CanBeClaimed() => ClaimedByWorkers.Count < MaxWorkers;
    public void Claim(Character worker)
    {
        if (!ClaimedByWorkers.Contains(worker)) ClaimedByWorkers.Add(worker);
    }
    public void Unclaim(Character worker)
    {
        ClaimedByWorkers.Remove(worker);
    }
}
```

If the file already has `using` directives, add `using MWI.Quests;` to the existing block. If `Character.CharacterId` doesn't exist as a string property, replace with whatever the project uses for character ids (check `Character.cs` — in the wage spec `BuildingId` was a NetworkVariable; characters likely have similar `CharacterId` exposed on the Character class).

- [ ] **Step 3: Verify compile**

`assets-refresh` + `console-get-logs`. Expected: zero NEW errors. If `CharacterId` doesn't exist, compile breaks with CS1061 — read `Character.cs` to find the actual property and replace.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs
git commit -m "feat(quests): BuildingTask implements IQuest (Hybrid C unification)

Adds QuestId (auto-Guid), Issuer, OriginWorldId/MapId, per-character
Contribution dict, TryJoin/TryLeave/RecordProgress mutators, and the
QuestState computed from existing IsValid + IsCompleted state. Existing
fields (Target, MaxWorkers, ClaimedByWorkers, Claim/Unclaim) preserved
unchanged. Subclass override hooks: Title, InstructionLine, Description,
QuestTarget, IsCompletedInternal."
```

### Task 9: HarvestResourceTask IQuest specifics

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Tasks/HarvestResourceTask.cs`

- [ ] **Step 1: Override IQuest specifics**

Replace the class with:

```csharp
using MWI.Quests;

public class HarvestResourceTask : BuildingTask
{
    private Harvestable _harvestableTarget;
    public override int MaxWorkers => 10;

    public override string Title => "Harvest Resource";
    public override string InstructionLine
    {
        get
        {
            string itemName = _harvestableTarget != null ? _harvestableTarget.name : "<destroyed>";
            return $"Harvest {Required} {itemName}";
        }
    }
    public override string Description =>
        $"Harvest from {(_harvestableTarget != null ? _harvestableTarget.name : "<destroyed>")} until depleted.";

    public override int Required =>
        _harvestableTarget != null ? _harvestableTarget.RemainingYield : 0;

    public HarvestResourceTask(Harvestable target) : base()
    {
        _harvestableTarget = target;
        Target = target;
        QuestTarget = new HarvestableTarget(target);
    }

    public override bool IsValid() =>
        _harvestableTarget != null && _harvestableTarget.CanHarvest();
}
```

If the existing constructor signature differs (e.g., `: base(target)`), adjust accordingly — the goal is `Target = target` AND `QuestTarget = new HarvestableTarget(target)`.

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/World/Buildings/Tasks/HarvestResourceTask.cs
git commit -m "feat(quests): HarvestResourceTask provides IQuest specifics + HarvestableTarget"
```

### Task 10: PickupLooseItemTask IQuest specifics

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Tasks/PickupLooseItemTask.cs`

- [ ] **Step 1: Override IQuest specifics**

Replace the class with:

```csharp
using MWI.Quests;

public class PickupLooseItemTask : BuildingTask
{
    private WorldItem _worldItemTarget;

    public override string Title => "Pick Up Item";
    public override string InstructionLine
    {
        get
        {
            string itemName = _worldItemTarget != null && _worldItemTarget.ItemInstance != null
                ? _worldItemTarget.ItemInstance.ItemSO.name
                : "<destroyed>";
            return $"Pick up {itemName} and return to storage.";
        }
    }
    public override string Description => InstructionLine;

    public PickupLooseItemTask(WorldItem target) : base()
    {
        _worldItemTarget = target;
        Target = target;
        QuestTarget = new WorldItemTarget(target);
    }

    public override bool IsValid() =>
        _worldItemTarget != null && !_worldItemTarget.IsBeingCarried;
}
```

If `WorldItem.IsBeingCarried` doesn't exist, replace with whatever predicate prevents claim (likely something like `!IsClaimedForPickup` or `!IsHeld`).

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/World/Buildings/Tasks/PickupLooseItemTask.cs
git commit -m "feat(quests): PickupLooseItemTask provides IQuest specifics + WorldItemTarget"
```

### Task 11: BuyOrder implements IQuest

**Files:**
- Modify: `Assets/Scripts/World/Jobs/BuyOrder.cs`

- [ ] **Step 1: Add IQuest implementation**

Add `using MWI.Quests;` and `using System;` and `using System.Collections.Generic;` if missing. Modify the class declaration:

```csharp
[System.Serializable]
public class BuyOrder : MWI.Quests.IQuest
{
    // === existing fields (unchanged) ===
    public ItemSO ItemToTransport { get; private set; }
    public int Quantity { get; private set; }
    public CommercialBuilding Source { get; private set; }
    public CommercialBuilding Destination { get; private set; }
    public int RemainingDays { get; private set; }
    public Character ClientBoss { get; private set; }
    public int DeliveredQuantity { get; private set; }
    public bool IsCompleted => DeliveredQuantity >= Quantity;

    // ... existing constructors / methods unchanged ...

    // === IQuest implementation ===

    public string QuestId { get; private set; } = System.Guid.NewGuid().ToString("N");
    public string OriginWorldId { get; set; } = string.Empty;  // settable so publisher can stamp
    public string OriginMapId { get; set; } = string.Empty;
    public Character Issuer { get; set; }
    public MWI.Quests.QuestType Type => MWI.Quests.QuestType.Job;

    public string Title => "Place Buy Order";
    public string InstructionLine
    {
        get
        {
            string item = ItemToTransport != null ? ItemToTransport.name : "<unknown>";
            string source = Source != null ? Source.BuildingDisplayName : "<unknown>";
            return $"Procure {Quantity} {item} from {source}.";
        }
    }
    public string Description =>
        $"Place a buy order at {(Source != null ? Source.BuildingDisplayName : "<unknown>")} for {Quantity} {(ItemToTransport != null ? ItemToTransport.name : "<unknown>")}.";

    public MWI.Quests.QuestState State
    {
        get
        {
            if (RemainingDays <= 0) return MWI.Quests.QuestState.Expired;
            if (IsCompleted) return MWI.Quests.QuestState.Completed;
            if (_contributors.Count >= MaxContributors) return MWI.Quests.QuestState.Full;
            return MWI.Quests.QuestState.Open;
        }
    }
    public bool IsExpired => RemainingDays <= 0;

    public int TotalProgress => DeliveredQuantity;
    public int Required => Quantity;
    public int MaxContributors => 1;  // one transporter per buy order

    private readonly List<Character> _contributors = new List<Character>();
    private readonly Dictionary<string, int> _contribution = new Dictionary<string, int>();
    public IReadOnlyList<Character> Contributors => _contributors;
    public IReadOnlyDictionary<string, int> Contribution => _contribution;

    private MWI.Quests.IQuestTarget _target;
    public MWI.Quests.IQuestTarget Target
    {
        get
        {
            if (_target == null && Source != null) _target = new MWI.Quests.BuildingTarget(Source);
            return _target;
        }
    }

    public event System.Action<MWI.Quests.IQuest> OnStateChanged;
    public event System.Action<MWI.Quests.IQuest, Character, int> OnProgressRecorded;

    public bool TryJoin(Character character)
    {
        if (character == null || _contributors.Count >= MaxContributors || _contributors.Contains(character)) return false;
        _contributors.Add(character);
        OnStateChanged?.Invoke(this);
        return true;
    }
    public bool TryLeave(Character character)
    {
        if (character == null) return false;
        bool removed = _contributors.Remove(character);
        if (removed) OnStateChanged?.Invoke(this);
        return removed;
    }
    public void RecordProgress(Character character, int amount)
    {
        if (character == null || amount <= 0) return;
        var id = character.CharacterId;
        if (string.IsNullOrEmpty(id)) return;
        _contribution.TryGetValue(id, out int prev);
        _contribution[id] = prev + amount;
        OnProgressRecorded?.Invoke(this, character, amount);
        if (IsCompleted) OnStateChanged?.Invoke(this);
    }
}
```

`Issuer` setter is internal — the publisher (CommercialBuilding) sets it when enqueuing the order. Same for `OriginWorldId` / `OriginMapId`.

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/World/Jobs/BuyOrder.cs
git commit -m "feat(quests): BuyOrder implements IQuest (MaxContributors=1, BuildingTarget)"
```

### Task 12: TransportOrder implements IQuest

**Files:**
- Modify: `Assets/Scripts/World/Jobs/TransportOrder.cs`

- [ ] **Step 1: Add IQuest implementation**

Same pattern as Task 11. Key difference: target points at Destination, not Source.

```csharp
[System.Serializable]
public class TransportOrder : MWI.Quests.IQuest
{
    // === existing fields ===
    public ItemSO ItemToTransport { get; private set; }
    public int Quantity { get; private set; }
    public CommercialBuilding Source { get; private set; }
    public CommercialBuilding Destination { get; private set; }
    public int DeliveredQuantity { get; private set; }
    public bool IsCompleted => DeliveredQuantity >= Quantity;

    // === IQuest implementation ===

    public string QuestId { get; private set; } = System.Guid.NewGuid().ToString("N");
    public string OriginWorldId { get; set; } = string.Empty;
    public string OriginMapId { get; set; } = string.Empty;
    public Character Issuer { get; set; }
    public MWI.Quests.QuestType Type => MWI.Quests.QuestType.Job;

    public string Title => "Transport Goods";
    public string InstructionLine
    {
        get
        {
            string item = ItemToTransport != null ? ItemToTransport.name : "<unknown>";
            string dest = Destination != null ? Destination.BuildingDisplayName : "<unknown>";
            return $"Deliver {Quantity} {item} to {dest}.";
        }
    }
    public string Description =>
        $"Load {Quantity} {(ItemToTransport != null ? ItemToTransport.name : "<unknown>")} from {(Source != null ? Source.BuildingDisplayName : "<unknown>")} and deliver to {(Destination != null ? Destination.BuildingDisplayName : "<unknown>")}.";

    public MWI.Quests.QuestState State
    {
        get
        {
            if (IsCompleted) return MWI.Quests.QuestState.Completed;
            if (_contributors.Count >= MaxContributors) return MWI.Quests.QuestState.Full;
            return MWI.Quests.QuestState.Open;
        }
    }
    public bool IsExpired => false;
    public int RemainingDays => int.MaxValue;  // TransportOrders don't expire

    public int TotalProgress => DeliveredQuantity;
    public int Required => Quantity;
    public int MaxContributors => 1;

    private readonly System.Collections.Generic.List<Character> _contributors = new();
    private readonly System.Collections.Generic.Dictionary<string, int> _contribution = new();
    public System.Collections.Generic.IReadOnlyList<Character> Contributors => _contributors;
    public System.Collections.Generic.IReadOnlyDictionary<string, int> Contribution => _contribution;

    private MWI.Quests.IQuestTarget _target;
    public MWI.Quests.IQuestTarget Target
    {
        get
        {
            if (_target == null && Destination != null) _target = new MWI.Quests.BuildingTarget(Destination);
            return _target;
        }
    }

    public event System.Action<MWI.Quests.IQuest> OnStateChanged;
    public event System.Action<MWI.Quests.IQuest, Character, int> OnProgressRecorded;

    public bool TryJoin(Character c) { if (c == null || _contributors.Count >= MaxContributors || _contributors.Contains(c)) return false; _contributors.Add(c); OnStateChanged?.Invoke(this); return true; }
    public bool TryLeave(Character c) { if (c == null) return false; bool r = _contributors.Remove(c); if (r) OnStateChanged?.Invoke(this); return r; }
    public void RecordProgress(Character c, int amount) { if (c == null || amount <= 0) return; var id = c.CharacterId; if (string.IsNullOrEmpty(id)) return; _contribution.TryGetValue(id, out int p); _contribution[id] = p + amount; OnProgressRecorded?.Invoke(this, c, amount); if (IsCompleted) OnStateChanged?.Invoke(this); }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/World/Jobs/TransportOrder.cs
git commit -m "feat(quests): TransportOrder implements IQuest (MaxContributors=1, target=Destination)"
```

### Task 13: CraftingOrder implements IQuest

**Files:**
- Modify: `Assets/Scripts/World/Jobs/CraftingOrder.cs`

- [ ] **Step 1: Add IQuest implementation**

Same pattern as Tasks 11-12. Key differences:
- `MaxContributors = int.MaxValue` (multiple crafters can chip)
- `Target = BuildingTarget(workshop)` — but CraftingOrder doesn't store workshop directly. Add a `Workshop` property if needed (or look up via the building that owns this order — typically `BuildingLogisticsManager` knows). For v1, **set the Workshop in the constructor** by adding a parameter:

```csharp
public CraftingOrder(ItemSO itemToCraft, int quantity, int remainingDays, Character clientBoss, CommercialBuilding workshop)
{
    ItemToCraft = itemToCraft;
    Quantity = quantity;
    RemainingDays = remainingDays;
    ClientBoss = clientBoss;
    Workshop = workshop;
}

public CommercialBuilding Workshop { get; private set; }
```

If the existing constructor doesn't take workshop, add it AND update all call sites (likely `BuildingLogisticsManager.PlaceCraftingOrder` — search for `new CraftingOrder(` and update each).

Then add IQuest implementation following the BuyOrder pattern, with:
- `MaxContributors => int.MaxValue;`
- `Target` returning `BuildingTarget(Workshop)`
- `Title => "Craft Items"`
- `InstructionLine => $"Craft {Quantity} {item} at {workshop}."`
- `TotalProgress => CraftedQuantity` (using existing CraftedQuantity field)

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/World/Jobs/CraftingOrder.cs Assets/Scripts/World/Buildings/Logistics/
git commit -m "feat(quests): CraftingOrder implements IQuest (multi-contributor, Workshop param)"
```

If you had to update call sites (PlaceCraftingOrder in LogisticsOrderBook or BuildingLogisticsManager), include those files in the commit.

---

## Phase 4 — Producer Events

### Task 14: BuildingTaskManager events

**Files:**
- Modify: `Assets/Scripts/World/Buildings/BuildingTaskManager.cs`

- [ ] **Step 1: Add events**

Open the file. Near the top of the class (around the `_availableTasks` field), add:

```csharp
public event System.Action<BuildingTask> OnTaskRegistered;
public event System.Action<BuildingTask, Character> OnTaskClaimed;
public event System.Action<BuildingTask, Character> OnTaskUnclaimed;
public event System.Action<BuildingTask> OnTaskCompleted;
```

Inside `RegisterTask(newTask)` (around line 19-30), at the end of the method:
```csharp
OnTaskRegistered?.Invoke(newTask);
```

Inside `ClaimBestTask<T>(...)` (around line 37+), after `task.Claim(worker)`:
```csharp
OnTaskClaimed?.Invoke(task, worker);
```

Inside `UnclaimTask(task, worker)` (around line 80+), after `task.Unclaim(worker)`:
```csharp
OnTaskUnclaimed?.Invoke(task, worker);
```

Inside `CompleteTask(task)` (find this method), after task removal:
```csharp
OnTaskCompleted?.Invoke(task);
```

- [ ] **Step 2: Verify compile + commit**

```bash
git add Assets/Scripts/World/Buildings/BuildingTaskManager.cs
git commit -m "feat(quests): add OnTaskRegistered/Claimed/Unclaimed/Completed events to BuildingTaskManager"
```

### Task 15: LogisticsOrderBook events

**Files:**
- Modify: `Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs`

- [ ] **Step 1: Add events**

Add near top:
```csharp
public event System.Action<BuyOrder> OnBuyOrderAdded;
public event System.Action<TransportOrder> OnTransportOrderAdded;
public event System.Action<CraftingOrder> OnCraftingOrderAdded;
public event System.Action<MWI.Quests.IQuest> OnAnyOrderRemoved;  // fired on completion / expiry
```

Fire each from the corresponding `Add*` method. For removals, fire `OnAnyOrderRemoved` from wherever orders are dropped from active lists (search for `_activeOrders.Remove`, `_placedBuyOrders.Remove`, etc.).

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/World/Buildings/Logistics/LogisticsOrderBook.cs
git commit -m "feat(quests): add Add/Remove events to LogisticsOrderBook"
```

### Task 16: CommercialBuilding aggregator (GetAvailableQuests, GetQuestById, ResolveIssuer, OnQuestPublished)

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

- [ ] **Step 1: Add the aggregator methods + events**

Near the top of `CommercialBuilding`:
```csharp
public event System.Action<MWI.Quests.IQuest> OnQuestPublished;
public event System.Action<MWI.Quests.IQuest> OnQuestStateChanged;
```

In `Awake()` (or `OnNetworkSpawn()` if that's where TaskManager/LogisticsManager are wired), subscribe to the Phase 4 events and forward as Quest events:

```csharp
private void HookQuestPublishingEvents()
{
    if (_taskManager != null)
    {
        _taskManager.OnTaskRegistered += task => PublishQuest(task);
    }
    if (_logisticsManager != null && _logisticsManager.OrderBook != null)
    {
        _logisticsManager.OrderBook.OnBuyOrderAdded += order => PublishQuest(order);
        _logisticsManager.OrderBook.OnTransportOrderAdded += order => PublishQuest(order);
        _logisticsManager.OrderBook.OnCraftingOrderAdded += order => PublishQuest(order);
    }
}

private void PublishQuest(MWI.Quests.IQuest quest)
{
    // Stamp issuer + world/map at publish time
    var issuer = ResolveIssuer();
    if (issuer != null && quest is BuyOrder bo) bo.Issuer = issuer;
    else if (issuer != null && quest is TransportOrder to) to.Issuer = issuer;
    else if (issuer != null && quest is CraftingOrder co) co.Issuer = issuer;
    else if (issuer != null && quest is BuildingTask bt) bt.Issuer = issuer;

    StampOriginWorldAndMap(quest);

    quest.OnStateChanged += FwdStateChanged;
    OnQuestPublished?.Invoke(quest);
}

private void FwdStateChanged(MWI.Quests.IQuest q) => OnQuestStateChanged?.Invoke(q);

private Character ResolveIssuer()
{
    // 1. LogisticsManager Character if present
    var lm = GetWorkerInRole(JobType.LogisticsManager);
    if (lm != null) return lm;
    // 2. Owner Character
    if (HasOwner) return GetOwner();
    // 3. null
    return null;
}

private void StampOriginWorldAndMap(MWI.Quests.IQuest quest)
{
    string mapId = MWI.WorldSystem.MapController.GetMapAtPosition(transform.position)?.MapId ?? string.Empty;
    string worldId = string.Empty;  // TODO: source from current WorldAssociation singleton when available
    if (quest is BuyOrder bo) { bo.OriginWorldId = worldId; bo.OriginMapId = mapId; }
    else if (quest is TransportOrder to) { to.OriginWorldId = worldId; to.OriginMapId = mapId; }
    else if (quest is CraftingOrder co) { co.OriginWorldId = worldId; co.OriginMapId = mapId; }
    else if (quest is BuildingTask bt) { bt.OriginWorldIdInternal(worldId, mapId); }  // see note below
}

public IEnumerable<MWI.Quests.IQuest> GetAvailableQuests()
{
    if (_taskManager != null)
    {
        foreach (var task in _taskManager.AvailableTasks) yield return task;
    }
    if (_logisticsManager != null && _logisticsManager.OrderBook != null)
    {
        foreach (var bo in _logisticsManager.OrderBook.PlacedBuyOrders) yield return bo;
        foreach (var to in _logisticsManager.OrderBook.PlacedTransportOrders) yield return to;
        foreach (var co in _logisticsManager.OrderBook.ActiveCraftingOrders) yield return co;
    }
}

public MWI.Quests.IQuest GetQuestById(string questId)
{
    foreach (var q in GetAvailableQuests())
    {
        if (q.QuestId == questId) return q;
    }
    return null;
}
```

For `BuildingTask` to receive Origin stamps, add a small internal helper to `BuildingTask.cs`:
```csharp
public void OriginWorldIdInternal(string worldId, string mapId) { OriginWorldId = worldId; OriginMapId = mapId; }
```

Don't fight the existing `protected set` — just add this explicit setter.

For `GetWorkerInRole(JobType)`: if it doesn't exist on CommercialBuilding, add it as a small helper that loops `AssignedWorkers` looking for the matching role, OR look it up via `_jobs[i].Worker` matching the role. Implementer adapts to the actual API.

If `MWI.WorldSystem.MapController.GetMapAtPosition` doesn't exist, use whatever spatial-map-lookup the project provides (search for `GetMapAtPosition` or `MapController.AllMaps`). Worst case fall back to: look at the building's interior registry record.

Call `HookQuestPublishingEvents()` from Awake or OnNetworkSpawn (whichever is appropriate — match the existing pattern for `_taskManager` / `_logisticsManager` initialization).

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs Assets/Scripts/World/Buildings/Tasks/BuildingTask.cs
git commit -m "feat(quests): CommercialBuilding aggregates Quest events + GetAvailableQuests + GetQuestById + ResolveIssuer"
```

---

## Phase 5 — CharacterQuestLog Subsystem

### Task 17: QuestLogSaveData + QuestSnapshotEntry DTOs

**Files:**
- Create: `Assets/Scripts/Character/CharacterQuestLog/QuestLogSaveData.cs`
- Create: `Assets/Scripts/Character/CharacterQuestLog/QuestSnapshotEntry.cs`

- [ ] **Step 1: Create QuestSnapshotEntry.cs**

```csharp
using System;
using UnityEngine;

[Serializable]
public class QuestSnapshotEntry
{
    public string questId;
    public string originWorldId;
    public string originMapId;
    public string issuerCharacterId;
    public int questType;          // QuestType enum as int

    public string title;
    public string instructionLine;
    public string description;

    public int totalProgress;
    public int required;
    public int maxContributors;
    public int myContribution;

    public int state;              // QuestState enum as int

    public Vector3 targetPosition;
    public bool hasZoneBounds;
    public Vector3 zoneCenter;
    public Vector3 zoneSize;
    public string targetDisplayName;
}
```

- [ ] **Step 2: Create QuestLogSaveData.cs**

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class QuestLogSaveData
{
    public List<QuestSnapshotEntry> activeQuests = new List<QuestSnapshotEntry>();
    public string focusedQuestId = string.Empty;
}
```

- [ ] **Step 3: Compile + commit (include all 4 .meta files: 2 .cs + 2 folder .meta if new)**

```bash
git add Assets/Scripts/Character/CharacterQuestLog/
git add Assets/Scripts/Character/CharacterQuestLog.meta
git commit -m "feat(questlog): add QuestLogSaveData + QuestSnapshotEntry DTOs"
```

Verify with `git show --stat <SHA>` — expect 5+ files.

### Task 18: CharacterQuestLog subsystem

**Files:**
- Create: `Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.Quests;

/// <summary>
/// Per-character quest log. Holds claimed quest references + denormalized snapshots
/// (so HUD can render even when source unloaded). Server-authoritative; clients
/// route mutations via ServerRpc; snapshots push via targeted ClientRpc.
/// </summary>
public class CharacterQuestLog : CharacterSystem, ICharacterSaveData<QuestLogSaveData>
{
    // Server-authoritative claimed-id list.
    private readonly NetworkList<FixedString64Bytes> _claimedQuestIds = new NetworkList<FixedString64Bytes>();

    // Server-authoritative focused-quest preference.
    private readonly NetworkVariable<FixedString64Bytes> _focusedQuestId = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Local snapshot cache keyed by quest id (server has live IQuest references; clients cache pushed snapshots).
    private readonly Dictionary<string, QuestSnapshotEntry> _snapshots = new Dictionary<string, QuestSnapshotEntry>();
    private readonly Dictionary<string, IQuest> _liveQuests = new Dictionary<string, IQuest>();  // server-only

    // Dormant snapshots loaded from save but not on the current map.
    private readonly Dictionary<string, QuestSnapshotEntry> _dormantSnapshots = new Dictionary<string, QuestSnapshotEntry>();

    public IReadOnlyList<IQuest> ActiveQuests
    {
        get
        {
            var list = new List<IQuest>(_liveQuests.Count);
            foreach (var q in _liveQuests.Values) list.Add(q);
            return list;
        }
    }

    public IQuest FocusedQuest
    {
        get
        {
            var id = _focusedQuestId.Value.ToString();
            return string.IsNullOrEmpty(id) ? null : (_liveQuests.TryGetValue(id, out var q) ? q : null);
        }
    }

    public IReadOnlyDictionary<string, QuestSnapshotEntry> Snapshots => _snapshots;
    public IReadOnlyDictionary<string, QuestSnapshotEntry> DormantSnapshots => _dormantSnapshots;

    public event Action<IQuest> OnQuestAdded;
    public event Action<IQuest> OnQuestRemoved;
    public event Action<IQuest> OnQuestProgressChanged;
    public event Action<IQuest> OnFocusedChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _claimedQuestIds.OnListChanged += HandleClaimedListChanged;
        _focusedQuestId.OnValueChanged += HandleFocusedChanged;
    }

    public override void OnNetworkDespawn()
    {
        _claimedQuestIds.OnListChanged -= HandleClaimedListChanged;
        _focusedQuestId.OnValueChanged -= HandleFocusedChanged;
        base.OnNetworkDespawn();
    }

    // === Mutations (server-authoritative) ===

    public bool TryClaim(IQuest quest)
    {
        if (quest == null) return false;
        if (!IsServer) { TryClaimServerRpc(quest.QuestId); return false; }
        return ServerTryClaim(quest);
    }

    private bool ServerTryClaim(IQuest quest)
    {
        if (quest == null || _liveQuests.ContainsKey(quest.QuestId)) return false;
        if (!quest.TryJoin(_character)) return false;
        _liveQuests[quest.QuestId] = quest;
        _claimedQuestIds.Add(new FixedString64Bytes(quest.QuestId));
        var snap = BuildSnapshot(quest);
        _snapshots[quest.QuestId] = snap;
        PushQuestSnapshotClientRpc(snap, RpcTargetForOwner());
        OnQuestAdded?.Invoke(quest);
        // Auto-focus most recent if nothing focused
        if (string.IsNullOrEmpty(_focusedQuestId.Value.ToString()))
            _focusedQuestId.Value = new FixedString64Bytes(quest.QuestId);
        // Subscribe to progress for HUD updates
        quest.OnProgressRecorded += HandleQuestProgress;
        quest.OnStateChanged += HandleQuestStateChanged;
        return true;
    }

    public bool TryAbandon(IQuest quest)
    {
        if (quest == null) return false;
        if (!IsServer) { TryAbandonServerRpc(quest.QuestId); return false; }
        return ServerTryAbandon(quest.QuestId);
    }

    private bool ServerTryAbandon(string questId)
    {
        if (!_liveQuests.TryGetValue(questId, out var quest)) return false;
        quest.TryLeave(_character);
        quest.OnProgressRecorded -= HandleQuestProgress;
        quest.OnStateChanged -= HandleQuestStateChanged;
        _liveQuests.Remove(questId);
        _snapshots.Remove(questId);
        for (int i = 0; i < _claimedQuestIds.Count; i++)
        {
            if (_claimedQuestIds[i].ToString() == questId) { _claimedQuestIds.RemoveAt(i); break; }
        }
        OnQuestRemoved?.Invoke(quest);
        // Re-focus to next active if abandoned was focused
        if (_focusedQuestId.Value.ToString() == questId)
        {
            _focusedQuestId.Value = _liveQuests.Count > 0
                ? new FixedString64Bytes(GetFirstQuestId())
                : default;
        }
        return true;
    }

    private string GetFirstQuestId()
    {
        foreach (var kv in _liveQuests) return kv.Key;
        return string.Empty;
    }

    public void SetFocused(IQuest quest)
    {
        if (!IsServer) { SetFocusedServerRpc(quest != null ? quest.QuestId : string.Empty); return; }
        _focusedQuestId.Value = new FixedString64Bytes(quest != null ? quest.QuestId : string.Empty);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TryClaimServerRpc(string questId)
    {
        // Resolve quest by id from the building system
        var quest = ResolveQuest(questId);
        if (quest != null) ServerTryClaim(quest);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TryAbandonServerRpc(string questId) => ServerTryAbandon(questId);

    [ServerRpc(RequireOwnership = false)]
    private void SetFocusedServerRpc(string questId) => _focusedQuestId.Value = new FixedString64Bytes(questId);

    private IQuest ResolveQuest(string questId)
    {
        // Server-side: find via BuildingManager.FindBuildingById on every commercial building? Too expensive.
        // For v1, when claiming we already have the quest reference (auto-claim hook passes it). Client-initiated
        // claim is rare in v1. Best-effort: scan all CommercialBuildings.
        foreach (var b in BuildingManager.Instance != null ? BuildingManager.Instance.AllBuildings : new System.Collections.Generic.List<Building>())
        {
            if (b is CommercialBuilding cb)
            {
                var q = cb.GetQuestById(questId);
                if (q != null) return q;
            }
        }
        return null;
    }

    private void HandleQuestProgress(IQuest quest, Character contributor, int amount)
    {
        // Update snapshot
        if (_snapshots.TryGetValue(quest.QuestId, out var snap))
        {
            snap.totalProgress = quest.TotalProgress;
            int my = quest.Contribution.TryGetValue(_character.CharacterId, out var c) ? c : 0;
            snap.myContribution = my;
            QuestProgressUpdatedClientRpc(quest.QuestId, snap.totalProgress, snap.myContribution, RpcTargetForOwner());
        }
        OnQuestProgressChanged?.Invoke(quest);
    }

    private void HandleQuestStateChanged(IQuest quest)
    {
        if (quest.State == QuestState.Completed || quest.State == QuestState.Expired)
        {
            ServerTryAbandon(quest.QuestId);
        }
    }

    private void HandleClaimedListChanged(NetworkListEvent<FixedString64Bytes> evt)
    {
        // Client-side reaction: when a quest id appears, wait for snapshot push; when one disappears, fire OnQuestRemoved.
        if (IsServer) return;
        if (evt.Type == NetworkListEvent<FixedString64Bytes>.EventType.Remove)
        {
            var id = evt.Value.ToString();
            _snapshots.Remove(id);
            // Synthesize a removal event using a stub quest? Better: HUD queries snapshots dict directly.
            OnQuestRemoved?.Invoke(null);  // TODO: emit a stub quest carrying the id
        }
    }

    private void HandleFocusedChanged(FixedString64Bytes prev, FixedString64Bytes next)
    {
        OnFocusedChanged?.Invoke(FocusedQuest);
    }

    [ClientRpc]
    private void PushQuestSnapshotClientRpc(QuestSnapshotEntry snap, ClientRpcParams target = default)
    {
        if (snap == null) return;
        _snapshots[snap.questId] = snap;
        OnQuestAdded?.Invoke(null);  // HUD reads from _snapshots dict
    }

    [ClientRpc]
    private void QuestProgressUpdatedClientRpc(string questId, int newTotal, int newMyContribution, ClientRpcParams target = default)
    {
        if (_snapshots.TryGetValue(questId, out var snap))
        {
            snap.totalProgress = newTotal;
            snap.myContribution = newMyContribution;
            OnQuestProgressChanged?.Invoke(null);
        }
    }

    private ClientRpcParams RpcTargetForOwner()
    {
        return new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
    }

    private QuestSnapshotEntry BuildSnapshot(IQuest q)
    {
        var snap = new QuestSnapshotEntry
        {
            questId = q.QuestId,
            originWorldId = q.OriginWorldId,
            originMapId = q.OriginMapId,
            issuerCharacterId = q.Issuer != null ? q.Issuer.CharacterId : string.Empty,
            questType = (int)q.Type,
            title = q.Title,
            instructionLine = q.InstructionLine,
            description = q.Description,
            totalProgress = q.TotalProgress,
            required = q.Required,
            maxContributors = q.MaxContributors,
            myContribution = q.Contribution.TryGetValue(_character.CharacterId, out var c) ? c : 0,
            state = (int)q.State,
            targetDisplayName = q.Target != null ? q.Target.GetDisplayName() : string.Empty,
        };
        if (q.Target != null)
        {
            snap.targetPosition = q.Target.GetWorldPosition();
            var bounds = q.Target.GetZoneBounds();
            if (bounds.HasValue)
            {
                snap.hasZoneBounds = true;
                snap.zoneCenter = bounds.Value.center;
                snap.zoneSize = bounds.Value.size;
            }
        }
        return snap;
    }

    // === ICharacterSaveData ===

    public string SaveKey => "CharacterQuestLog";
    public int LoadPriority => 70;

    public QuestLogSaveData Serialize()
    {
        var data = new QuestLogSaveData
        {
            focusedQuestId = _focusedQuestId.Value.ToString()
        };
        foreach (var snap in _snapshots.Values) data.activeQuests.Add(snap);
        // Include dormant snapshots so they survive multiple saves
        foreach (var snap in _dormantSnapshots.Values) data.activeQuests.Add(snap);
        return data;
    }

    public void Deserialize(QuestLogSaveData data)
    {
        _liveQuests.Clear();
        _snapshots.Clear();
        _dormantSnapshots.Clear();
        if (data == null || data.activeQuests == null) return;

        string currentMapId = ResolveCurrentMapId();
        foreach (var entry in data.activeQuests)
        {
            if (entry.originMapId == currentMapId)
            {
                // Try to resolve live quest
                var quest = ResolveQuest(entry.questId);
                if (quest != null && quest.State != QuestState.Completed && quest.State != QuestState.Expired)
                {
                    _liveQuests[entry.questId] = quest;
                    _snapshots[entry.questId] = entry;
                    quest.TryJoin(_character);
                    quest.OnProgressRecorded += HandleQuestProgress;
                    quest.OnStateChanged += HandleQuestStateChanged;
                }
                else
                {
                    Debug.LogWarning($"[CharacterQuestLog] Quest {entry.questId} no longer resolvable on current map; dropping.");
                }
            }
            else
            {
                _dormantSnapshots[entry.questId] = entry;
            }
        }
        // Restore focused
        if (!string.IsNullOrEmpty(data.focusedQuestId) && IsServer)
        {
            _focusedQuestId.Value = new FixedString64Bytes(data.focusedQuestId);
        }
    }

    private string ResolveCurrentMapId()
    {
        if (_character == null) return string.Empty;
        var tracker = _character.GetComponent<CharacterMapTracker>();
        return tracker != null ? tracker.CurrentMapID.Value.ToString() : string.Empty;
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
```

This is a large file (~250 lines). Implementer should commit it cleanly. Some of the network plumbing (`ResolveQuest` server-side scan) is best-effort for v1 — a future refactor will introduce a global `QuestRegistry` for O(1) lookup.

If `BuildingManager.Instance.AllBuildings` doesn't exist, replace with whatever the project uses (likely an iteration over `BuildingManager.Instance` known buildings — read `BuildingManager.cs` to confirm).

If `CharacterMapTracker` is on a child GameObject, use `GetComponentInChildren<CharacterMapTracker>()`.

- [ ] **Step 2: Compile + commit (include .meta!)**

```bash
git add Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs.meta
git commit -m "feat(questlog): implement CharacterQuestLog subsystem with NetworkList sync + ClientRpc snapshots + save/load reconciliation"
```

### Task 19: Expose CharacterQuestLog on Character facade

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 1: Add field + property**

Open `Character.cs`. Find the SerializeField subsystem block around lines 65-68. Insert after `_characterWorkLog`:

```csharp
[SerializeField] private CharacterQuestLog _characterQuestLog;
```

Find the property block around lines 234-237 (where `CharacterWallet` and `CharacterWorkLog` properties are defined). Insert after `CharacterWorkLog`:

```csharp
public CharacterQuestLog CharacterQuestLog => TryGet<CharacterQuestLog>(out var sQuestLog) ? sQuestLog : _characterQuestLog;
```

- [ ] **Step 2: Compile + commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(character): expose CharacterQuestLog on Character facade"
```

### Task 20: Attach CharacterQuestLog to Character_Default prefab (Unity Editor)

**Files:**
- Modify: `Assets/Prefabs/Character/Character_Default.prefab`

This is the same kind of Unity Editor MCP task as Task 11 in the wage plan. Use `mcp__ai-game-developer__assets-prefab-open` to open `Character_Default.prefab`, then:
1. Create child GameObject named `CharacterQuestLog` (sibling of `CharacterWallet`, `CharacterWorkLog`).
2. Add `CharacterQuestLog` component to it.
3. On the root `Character` component, set the `_characterQuestLog` field to the new child's `CharacterQuestLog` component.
4. On the child component, set its `_character` backref to the root Character (matches the existing convention).
5. Save and close the prefab.

Variants (Humanoid, Quadruped, Animal) inherit via the nested-prefab pattern.

- [ ] **Step 1: Edit prefab via MCP**

Run the MCP tool sequence above.

- [ ] **Step 2: Verify with `mcp__ai-game-developer__gameobject-find`**

Confirm both children exist with components attached and slots wired.

- [ ] **Step 3: Commit**

```bash
git add Assets/Prefabs/Character/Character_Default.prefab
git commit -m "feat(character): attach CharacterQuestLog subsystem to Character_Default prefab

Inherits to Humanoid/Quadruped/Animal variants via nested-prefab."
```

---

## Phase 6 — Auto-Claim Hook on Punch-In

### Task 21: CommercialBuilding auto-claim Quest hook

**Files:**
- Modify: `Assets/Scripts/World/Buildings/CommercialBuilding.cs`

- [ ] **Step 1: Subscribe to OnQuestPublished + add auto-claim filter**

Inside `WorkerStartingShift(Character worker)` (around line 432 per Task 20 grounding from the wage spec), AFTER the existing punch-in + WorkLog hook, add:

```csharp
// Quest system: auto-claim eligible published quests for this worker.
if (worker != null && worker.CharacterQuestLog != null)
{
    foreach (var quest in GetAvailableQuests())
    {
        if (IsQuestEligibleForWorker(quest, worker))
        {
            worker.CharacterQuestLog.TryClaim(quest);
        }
    }
    // Also subscribe so future-published quests during this shift auto-claim
    OnQuestPublished += quest => TryAutoClaimForOnShiftWorker(quest, worker);
}
```

Add the helper method:
```csharp
private void TryAutoClaimForOnShiftWorker(MWI.Quests.IQuest quest, Character worker)
{
    if (worker == null || worker.CharacterQuestLog == null) return;
    if (!IsQuestEligibleForWorker(quest, worker)) return;
    worker.CharacterQuestLog.TryClaim(quest);
}

private bool IsQuestEligibleForWorker(MWI.Quests.IQuest quest, Character worker)
{
    if (quest == null || worker == null) return false;
    if (quest.State != MWI.Quests.QuestState.Open) return false;

    // Find the worker's job role at this building
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
    // v1 mapping: harvest tasks → harvester family; pickup → logistics; orders → logistics
    // and craft orders → crafter family. Refine as new jobs land.
    if (quest is HarvestResourceTask)
    {
        return jobType == MWI.WorldSystem.JobType.Woodcutter
            || jobType == MWI.WorldSystem.JobType.Miner
            || jobType == MWI.WorldSystem.JobType.Forager
            || jobType == MWI.WorldSystem.JobType.Farmer;
    }
    if (quest is PickupLooseItemTask) return jobType == MWI.WorldSystem.JobType.LogisticsManager || jobType == MWI.WorldSystem.JobType.Transporter;
    if (quest is BuyOrder) return jobType == MWI.WorldSystem.JobType.LogisticsManager;
    if (quest is TransportOrder) return jobType == MWI.WorldSystem.JobType.Transporter;
    if (quest is CraftingOrder) return jobType == MWI.WorldSystem.JobType.Crafter || jobType == MWI.WorldSystem.JobType.Blacksmith || jobType == MWI.WorldSystem.JobType.BlacksmithApprentice;
    return false;
}
```

Also clean up the subscription in `WorkerEndingShift` so it doesn't leak — tricky because the lambda captures `worker`. Simplest fix: store `(Character, Action<IQuest>)` pairs in a Dictionary and remove on punch-out. Implementer's choice on the cleanest pattern.

- [ ] **Step 2: Verify compile + commit**

```bash
git add Assets/Scripts/World/Buildings/CommercialBuilding.cs
git commit -m "feat(quests): auto-claim eligible quests for on-shift workers (player + NPC)"
```

---

## Phase 7 — HUD Widgets

### Task 22: UI_QuestTracker prefab + script

**Files:**
- Create: `Assets/Scripts/UI/Quest/UI_QuestTracker.cs`
- Create: `Assets/Prefabs/UI/Quest/UI_QuestTracker.prefab` (Unity Editor)

- [ ] **Step 1: Create the script**

```csharp
using TMPro;
using UnityEngine;

public class UI_QuestTracker : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _instructionText;
    [SerializeField] private GameObject _root;

    private CharacterQuestLog _log;

    public void Initialize(CharacterQuestLog log)
    {
        if (_log != null)
        {
            _log.OnFocusedChanged -= HandleFocusChanged;
            _log.OnQuestProgressChanged -= HandleProgressChanged;
        }
        _log = log;
        if (_log != null)
        {
            _log.OnFocusedChanged += HandleFocusChanged;
            _log.OnQuestProgressChanged += HandleProgressChanged;
        }
        Refresh();
    }

    private void HandleFocusChanged(MWI.Quests.IQuest quest) => Refresh();
    private void HandleProgressChanged(MWI.Quests.IQuest quest) => Refresh();

    private void Refresh()
    {
        if (_log == null || _log.FocusedQuest == null)
        {
            if (_root != null) _root.SetActive(false);
            return;
        }
        if (_root != null) _root.SetActive(true);
        var q = _log.FocusedQuest;
        if (_titleText != null) _titleText.text = q.Title;
        if (_instructionText != null)
        {
            string line = q.InstructionLine;
            if (q.Required > 0 && q.Required != int.MaxValue)
                line += $" ({q.TotalProgress} / {q.Required})";
            _instructionText.text = line;
        }
    }

    private void OnDestroy()
    {
        if (_log != null)
        {
            _log.OnFocusedChanged -= HandleFocusChanged;
            _log.OnQuestProgressChanged -= HandleProgressChanged;
        }
    }
}
```

- [ ] **Step 2: Create the prefab via MCP (Unity Editor)**

Use `mcp__ai-game-developer__assets-prefab-create` (or manually):
1. Create a new Canvas-child Panel anchored top-right, size ~280x60.
2. Add two `TextMeshProUGUI` children — one for title (bold, larger), one for instruction (smaller, lighter).
3. Add `UI_QuestTracker` component to the root; wire `_titleText`, `_instructionText`, `_root`.
4. Save as `Assets/Prefabs/UI/Quest/UI_QuestTracker.prefab`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/Quest/UI_QuestTracker.cs Assets/Scripts/UI/Quest/UI_QuestTracker.cs.meta Assets/Prefabs/UI/Quest/
git commit -m "feat(hud): add UI_QuestTracker widget (top-right, title + instruction line)"
```

### Task 23: UI_QuestLogWindow prefab + script

**Files:**
- Create: `Assets/Scripts/UI/Quest/UI_QuestLogWindow.cs`
- Create: `Assets/Prefabs/UI/Quest/UI_QuestLogWindow.prefab` (Unity Editor)

- [ ] **Step 1: Create the script**

```csharp
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_QuestLogWindow : UI_WindowBase
{
    [SerializeField] private RectTransform _listParent;
    [SerializeField] private GameObject _listEntryPrefab;  // a small prefab with TMP + button per quest
    [SerializeField] private TextMeshProUGUI _detailsTitle;
    [SerializeField] private TextMeshProUGUI _detailsBody;
    [SerializeField] private TextMeshProUGUI _detailsContributors;
    [SerializeField] private Button _setFocusedButton;
    [SerializeField] private Button _abandonButton;

    private CharacterQuestLog _log;
    private MWI.Quests.IQuest _selected;

    public void Initialize(CharacterQuestLog log)
    {
        _log = log;
        if (_log != null)
        {
            _log.OnQuestAdded += _ => RefreshList();
            _log.OnQuestRemoved += _ => RefreshList();
            _log.OnQuestProgressChanged += _ => RefreshDetails();
        }
        RefreshList();
    }

    public void RefreshList()
    {
        if (_listParent == null) return;
        // Clear existing children
        for (int i = _listParent.childCount - 1; i >= 0; i--)
            Destroy(_listParent.GetChild(i).gameObject);

        if (_log == null) return;
        foreach (var q in _log.ActiveQuests)
        {
            if (_listEntryPrefab == null) continue;
            var entry = Instantiate(_listEntryPrefab, _listParent);
            var label = entry.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = q.Title;
            var btn = entry.GetComponent<Button>();
            if (btn != null)
            {
                var captured = q;
                btn.onClick.AddListener(() => SelectQuest(captured));
            }
        }
    }

    private void SelectQuest(MWI.Quests.IQuest quest)
    {
        _selected = quest;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        if (_selected == null) { ClearDetails(); return; }
        if (_detailsTitle != null) _detailsTitle.text = _selected.Title;
        if (_detailsBody != null) _detailsBody.text = $"{_selected.InstructionLine}\n\n{_selected.Description}";
        if (_detailsContributors != null)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Contributors ({_selected.Contributors.Count}):");
            foreach (var c in _selected.Contributors)
            {
                int contrib = _selected.Contribution.TryGetValue(c.CharacterId, out var v) ? v : 0;
                sb.AppendLine($"  • {c.CharacterName} ({contrib})");
            }
            _detailsContributors.text = sb.ToString();
        }

        _setFocusedButton?.onClick.RemoveAllListeners();
        _setFocusedButton?.onClick.AddListener(() => _log?.SetFocused(_selected));

        _abandonButton?.onClick.RemoveAllListeners();
        _abandonButton?.onClick.AddListener(() =>
        {
            if (_selected != null && _log != null) _log.TryAbandon(_selected);
            _selected = null;
            ClearDetails();
        });
    }

    private void ClearDetails()
    {
        if (_detailsTitle != null) _detailsTitle.text = "";
        if (_detailsBody != null) _detailsBody.text = "";
        if (_detailsContributors != null) _detailsContributors.text = "";
    }
}
```

- [ ] **Step 2: Create the prefab via MCP**

Build a 2-column window prefab with:
- Left RectTransform (`_listParent`) with VerticalLayoutGroup.
- Right column with TMP children for title, body, contributors.
- Two buttons (Set as Focused, Abandon).
- A small list-entry prefab (TMP + Button) wired as `_listEntryPrefab`.

Save as `Assets/Prefabs/UI/Quest/UI_QuestLogWindow.prefab`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/Quest/UI_QuestLogWindow.cs Assets/Scripts/UI/Quest/UI_QuestLogWindow.cs.meta Assets/Prefabs/UI/Quest/UI_QuestLogWindow.prefab
git commit -m "feat(hud): add UI_QuestLogWindow (2-col list/details, abandon, set focused)"
```

### Task 24: PlayerUI registration

**Files:**
- Modify: `Assets/Scripts/UI/PlayerUI.cs`

- [ ] **Step 1: Add fields + initialize wiring**

Find the SerializeField UI Windows block (around line 33-37). Add:
```csharp
[SerializeField] private UI_QuestTracker _questTrackerUI;
[SerializeField] private UI_QuestLogWindow _questLogWindow;
```

In `Initialize(playerCharacter)` after the existing widget wiring:
```csharp
if (_questTrackerUI != null)
{
    _questTrackerUI.Initialize(playerCharacter.CharacterQuestLog);
}
if (_questLogWindow != null)
{
    _questLogWindow.Initialize(playerCharacter.CharacterQuestLog);
    _questLogWindow.CloseWindow();  // start hidden; bind L key to open elsewhere
}
```

For L-key binding: add a quick `Update()` check inside PlayerUI (or wherever input handling lives):
```csharp
private void Update()
{
    if (_questLogWindow != null && Input.GetKeyDown(KeyCode.L))
    {
        if (_questLogWindow.gameObject.activeSelf) _questLogWindow.CloseWindow();
        else _questLogWindow.OpenWindow();
    }
}
```

If PlayerUI already has an Update method, fold the check in.

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(hud): register Quest tracker + log window on PlayerUI"
```

---

## Phase 8 — World Markers

### Task 25: QuestWorldMarkerRenderer + 3 marker prefabs

**Files:**
- Create: `Assets/Scripts/UI/Quest/QuestWorldMarkerRenderer.cs`
- Create: `Assets/Prefabs/UI/Quest/QuestMarker_Diamond.prefab` (Unity Editor)
- Create: `Assets/Prefabs/UI/Quest/QuestMarker_Beacon.prefab` (Unity Editor)
- Create: `Assets/Prefabs/UI/Quest/QuestZone_Fill.prefab` (Unity Editor)

- [ ] **Step 1: Create the renderer script**

```csharp
using System.Collections.Generic;
using UnityEngine;
using MWI.Quests;

public class QuestWorldMarkerRenderer : MonoBehaviour
{
    [SerializeField] private GameObject _diamondPrefab;
    [SerializeField] private GameObject _beaconPrefab;
    [SerializeField] private GameObject _zoneFillPrefab;

    private CharacterQuestLog _log;
    private CharacterMapTracker _mapTracker;
    private readonly Dictionary<string, List<GameObject>> _spawnedMarkers = new Dictionary<string, List<GameObject>>();

    public void Initialize(CharacterQuestLog log, CharacterMapTracker mapTracker)
    {
        if (_log != null)
        {
            _log.OnQuestAdded -= HandleQuestAdded;
            _log.OnQuestRemoved -= HandleQuestRemoved;
        }
        _log = log;
        _mapTracker = mapTracker;
        if (_log != null)
        {
            _log.OnQuestAdded += HandleQuestAdded;
            _log.OnQuestRemoved += HandleQuestRemoved;
        }
        if (_mapTracker != null)
        {
            _mapTracker.CurrentMapID.OnValueChanged += HandleMapChanged;
        }
        RefreshAll();
    }

    private void HandleQuestAdded(IQuest quest) => RefreshAll();
    private void HandleQuestRemoved(IQuest quest) => RefreshAll();
    private void HandleMapChanged(Unity.Collections.FixedString128Bytes prev, Unity.Collections.FixedString128Bytes next) => RefreshAll();

    private void RefreshAll()
    {
        ClearAll();
        if (_log == null) return;
        string currentMapId = _mapTracker != null ? _mapTracker.CurrentMapID.Value.ToString() : string.Empty;
        foreach (var q in _log.ActiveQuests)
        {
            if (q == null || q.Target == null) continue;
            if (!string.IsNullOrEmpty(currentMapId) && q.OriginMapId != currentMapId) continue;
            SpawnMarkersFor(q);
        }
    }

    private void SpawnMarkersFor(IQuest quest)
    {
        var spawned = new List<GameObject>();
        var t = quest.Target;

        var zoneBounds = t.GetZoneBounds();
        if (zoneBounds.HasValue && _zoneFillPrefab != null)
        {
            var fill = Instantiate(_zoneFillPrefab, zoneBounds.Value.center, Quaternion.identity);
            fill.transform.localScale = new Vector3(zoneBounds.Value.size.x, 0.1f, zoneBounds.Value.size.z);
            spawned.Add(fill);
        }

        var moveTarget = t.GetMovementTarget();
        if (moveTarget.HasValue && _beaconPrefab != null)
        {
            var beacon = Instantiate(_beaconPrefab, moveTarget.Value, Quaternion.identity);
            spawned.Add(beacon);
        }
        else if (!zoneBounds.HasValue && _diamondPrefab != null)
        {
            // Object/action target — diamond above the world position
            var pos = t.GetWorldPosition() + Vector3.up * 2f;
            var diamond = Instantiate(_diamondPrefab, pos, Quaternion.identity);
            spawned.Add(diamond);
        }

        if (spawned.Count > 0) _spawnedMarkers[quest.QuestId] = spawned;
    }

    private void ClearAll()
    {
        foreach (var list in _spawnedMarkers.Values)
            foreach (var go in list) if (go != null) Destroy(go);
        _spawnedMarkers.Clear();
    }

    private void OnDestroy()
    {
        ClearAll();
        if (_log != null)
        {
            _log.OnQuestAdded -= HandleQuestAdded;
            _log.OnQuestRemoved -= HandleQuestRemoved;
        }
        if (_mapTracker != null)
        {
            _mapTracker.CurrentMapID.OnValueChanged -= HandleMapChanged;
        }
    }
}
```

- [ ] **Step 2: Create the 3 marker prefabs via MCP**

For each prefab:
- **QuestMarker_Diamond**: a Quad or small mesh with a billboard script + gold material; pulse animation via simple AnimationCurve / scripted lerp.
- **QuestMarker_Beacon**: tall transparent vertical quad (light shaft) + ground decal ring.
- **QuestZone_Fill**: flat quad with a semi-transparent gold material; the renderer scales it to the zone bounds.

Quick visual fidelity — these are placeholder prefabs that match the style picked in brainstorming. Polish pass later.

- [ ] **Step 3: Register on PlayerUI** (or HUD canvas)

Add `_questMarkerRenderer` SerializeField on PlayerUI; in `Initialize(playerCharacter)`:
```csharp
if (_questMarkerRenderer != null)
{
    _questMarkerRenderer.Initialize(playerCharacter.CharacterQuestLog, playerCharacter.GetComponent<CharacterMapTracker>());
}
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/UI/Quest/QuestWorldMarkerRenderer.cs Assets/Scripts/UI/Quest/QuestWorldMarkerRenderer.cs.meta Assets/Prefabs/UI/Quest/ Assets/Scripts/UI/PlayerUI.cs
git commit -m "feat(hud): add QuestWorldMarkerRenderer + 3 marker prefabs (diamond, beacon, zone fill)

Map-aware filtering — markers stop drawing when player crosses map boundary."
```

### Task 26: GameScene wiring (Unity Editor)

**Files:**
- Modify: `Assets/Scenes/GameScene.unity`

- [ ] **Step 1: Add UI_QuestTracker + UI_QuestLogWindow under PlayerUI canvas**

Open `GameScene.unity`. Find the `UI_PlayerHUD` GameObject (per the wage spec exploration). Instantiate the `UI_QuestTracker.prefab` and `UI_QuestLogWindow.prefab` as children. Wire references on the `PlayerUI` script: `_questTrackerUI` → tracker instance, `_questLogWindow` → log window instance, `_questMarkerRenderer` → renderer instance (or add the marker renderer as a separate root GameObject).

- [ ] **Step 2: Save scene + commit**

```bash
git add Assets/Scenes/GameScene.unity
git commit -m "feat(hud): wire Quest tracker + log + marker renderer into GameScene"
```

---

## Phase 9 — Save / Map-Transition Reconciliation

### Task 27: Map-transition wake-up reconciliation

**Files:**
- Modify: `Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs`

- [ ] **Step 1: Subscribe to CharacterMapTracker changes + replay dormant snapshots**

Inside `OnNetworkSpawn()` (after existing event subscriptions):

```csharp
var tracker = _character.GetComponent<CharacterMapTracker>();
if (tracker != null)
{
    tracker.CurrentMapID.OnValueChanged += HandleMapChanged;
}
```

Add the handler:
```csharp
private void HandleMapChanged(Unity.Collections.FixedString128Bytes prev, Unity.Collections.FixedString128Bytes next)
{
    string newMapId = next.ToString();
    // Promote dormant snapshots whose originMapId now matches
    var promoted = new List<string>();
    foreach (var kv in _dormantSnapshots)
    {
        if (kv.Value.originMapId == newMapId)
        {
            var quest = ResolveQuest(kv.Key);
            if (quest != null && quest.State != QuestState.Completed && quest.State != QuestState.Expired)
            {
                _liveQuests[kv.Key] = quest;
                _snapshots[kv.Key] = kv.Value;
                quest.TryJoin(_character);
                quest.OnProgressRecorded += HandleQuestProgress;
                quest.OnStateChanged += HandleQuestStateChanged;
                OnQuestAdded?.Invoke(quest);
                promoted.Add(kv.Key);
            }
        }
    }
    foreach (var id in promoted) _dormantSnapshots.Remove(id);
}
```

Unsubscribe in `OnNetworkDespawn()`.

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Character/CharacterQuestLog/CharacterQuestLog.cs
git commit -m "feat(questlog): wake-up reconciliation on map change (promote matching dormant snapshots)"
```

---

## Phase 10 — Smoke Test

### Task 28: Manual smoke test plan

**Files:**
- Create: `docs/superpowers/smoketests/2026-04-23-quest-system-smoketest.md`

- [ ] **Step 1: Write the smoke test plan**

Mirror the wage system smoke test format. Eight Play Mode scenarios:
1. **Player Harvester** — take a Woodcutter job at a sawmill; quest auto-claims; zone highlight + diamond markers appear; chop logs; progress updates in tracker; quest completes; another auto-publishes.
2. **Player Transporter** — take a Transporter job; BuildingTarget beacon appears at destination; deliver; progress + completion.
3. **Player Crafter** — take a Blacksmith job; CraftingOrder appears; multiple players can chip (shared); progress shows shared contributions.
4. **Abandon** — abandon a quest; quest disappears from log; auto-claim picks up the next eligible.
5. **Save/Load** — save with active quest; reload; quest restored; abandon button works.
6. **Map transition** — claim quest on Map A; travel to Map B; markers disappear, quest shows "Pending — return to Map A"; travel back; quest reactivates.
7. **Multiplayer** — Host + Client both Harvesters at same sawmill; both auto-claim same Quest (shared, MaxContributors=10); both see contribution progress in their tracker.
8. **Late-joiner** — Client joins session after another client claimed a quest; Client sees own claimed quests on join.

For each scenario: setup, run steps, expected, failure modes.

Same structure as `docs/superpowers/smoketests/2026-04-22-worker-wages-and-performance-smoketest.md`.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/smoketests/2026-04-23-quest-system-smoketest.md
git commit -m "docs(test): add manual smoke-test plan for quest system"
```

---

## Phase 11 — Documentation

### Task 29: New `quest-system` SKILL.md

**Files:**
- Create: `.agent/skills/quest-system/SKILL.md`

- [ ] **Step 1: Write the SKILL.md**

Cover (mirror the wage-system SKILL.md structure):
- What it is (unified Quest primitive)
- When to use this skill
- Public API (IQuest, IQuestTarget, CharacterQuestLog)
- The Hybrid C unification — BuildingTask + orders implement IQuest directly
- Integration points (where producers fire OnQuestPublished)
- HUD layer overview (tracker / log / marker renderer + map-aware filtering)
- Save/load (LoadPriority 70)
- Network sync (NetworkList + ClientRpc snapshots, late-joiner gap)
- Gotchas (issuer fallback chain, dormant snapshots, no central QuestRegistry yet)

~150 lines. Cross-link to the wiki page + spec.

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/quest-system/SKILL.md
git commit -m "docs(skill): add SKILL.md for quest-system"
```

### Task 30: Update existing 4 SKILL.md files

**Files:**
- Modify: `.agent/skills/job_system/SKILL.md`
- Modify: `.agent/skills/logistics_cycle/SKILL.md`
- Modify: `.agent/skills/save-load-system/SKILL.md`
- Modify: `.agent/skills/player_ui/SKILL.md`

Append a `## Quest Integration` section to each, ~10-30 lines per file. Cross-link to `.agent/skills/quest-system/SKILL.md`.

- [ ] **Step 1: Edit each file**

For each: read current state, append the new section near the bottom. Specific content per file:

- **job_system**: BuildingTask now implements IQuest; CharacterQuestLog auto-claim path on player on-shift.
- **logistics_cycle**: BuyOrder / TransportOrder / CraftingOrder now implement IQuest; OrderBook fires OnXxxAdded events.
- **save-load-system**: CharacterQuestLog adds SaveKey "CharacterQuestLog", LoadPriority 70.
- **player_ui**: New widgets UI_QuestTracker, UI_QuestLogWindow, QuestWorldMarkerRenderer.

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/job_system/ .agent/skills/logistics_cycle/ .agent/skills/save-load-system/ .agent/skills/player_ui/
git commit -m "docs(skill): cross-link existing SKILLs to quest-system"
```

### Task 31: New wiki page `wiki/systems/quest-system.md`

**Files:**
- Create: `wiki/systems/quest-system.md`

- [ ] **Step 1: Write the wiki page**

Use `wiki/_templates/system.md` template. Required sections (per `wiki/CLAUDE.md`):
- Frontmatter (type=system, primary_agent, owner_code_path, depends_on, depended_on_by, sources, related)
- Summary
- Purpose
- Responsibilities (+ Non-responsibilities)
- Key classes / files
- Public API / entry points
- Data flow
- Dependencies (Upstream / Downstream)
- State & persistence
- Known gotchas
- Open questions / TODO
- Change log
- Sources

Mirror the structure of `wiki/systems/worker-wages-and-performance.md`.

- [ ] **Step 2: Commit**

```bash
git add wiki/systems/quest-system.md
git commit -m "docs(wiki): add quest-system architecture page"
```

### Task 32: Update existing wiki pages

**Files:**
- Modify: `wiki/systems/jobs-and-logistics.md`
- Modify: `wiki/systems/commercial-building.md`
- Modify: `wiki/systems/building-logistics-manager.md`
- Modify: `wiki/systems/building-task-manager.md`
- Modify: `wiki/systems/worker-wages-and-performance.md`

For each:
- Bump `updated:` to today's date.
- Add `[[quest-system]]` to `related` and `depends_on` / `depended_on_by` as appropriate.
- Append a change-log entry: `- 2026-04-23 — Quest integration added: <brief> — claude`.

- [ ] **Step 1: Edit each file**

Surgical edits only — change-log lines + frontmatter wikilinks.

- [ ] **Step 2: Commit**

```bash
git add wiki/systems/
git commit -m "docs(wiki): cross-link existing system pages to quest-system"
```

### Task 33: Update specialized agents

**Files:**
- Modify: `.claude/agents/building-furniture-specialist.md`
- Modify: `.claude/agents/npc-ai-specialist.md`

Add a `2026-04-23 — Quest System` entry to the Recent changes section of each agent. Cover:

- **building-furniture-specialist**: BuildingTask now implements IQuest; orders too; CommercialBuilding has GetAvailableQuests / OnQuestPublished / TrySetAssignmentWage style aggregation; auto-claim hook on WorkerStartingShift.
- **npc-ai-specialist**: BuildingTaskManager.ClaimBestTask<T> still returns the same types but they're now also IQuest; nothing changes for GOAP claim sites; CharacterQuestLog tracks server-side for both NPC and player alike but only the local player's HUD subscribes.

- [ ] **Step 1: Edit each agent**

- [ ] **Step 2: Commit**

```bash
git add .claude/agents/building-furniture-specialist.md .claude/agents/npc-ai-specialist.md
git commit -m "docs(agents): teach building + NPC specialists about quest system"
```

---

## Phase 12 — Final Verification

### Task 34: Final verification pass

- [ ] **Step 1: Compile clean across the whole project**

`mcp__ai-game-developer__assets-refresh` + `mcp__ai-game-developer__console-get-logs`. Zero NEW errors (pre-existing warnings about path-with-spaces are OK).

- [ ] **Step 2: Run any EditMode tests**

`mcp__ai-game-developer__tests-run` with `testMode: EditMode`. Wage tests (14) should still pass.

- [ ] **Step 3: Verify all expected files exist**

Spot-check via `git status` + `find Assets/Scripts/Quest`:
- `IQuest.cs`, `IQuestTarget.cs`
- 5 target wrappers under `Targets/`
- `CharacterQuestLog.cs`, `QuestLogSaveData.cs`, `QuestSnapshotEntry.cs`
- 3 UI scripts + 5 prefabs (3 markers + tracker + log window)
- All .meta files committed

- [ ] **Step 4: Spot-check the wage system still works**

Open `Character_Default.prefab`. Confirm `CharacterWallet`, `CharacterWorkLog`, `CharacterQuestLog` all exist as children with components and slots wired.

Open `GameScene`. Confirm `WageSystemService` is still in the scene + new quest UI is wired under the player HUD.

- [ ] **Step 5: Final commit (if any docs changes / cleanup)**

```bash
git add -A
git commit -m "chore: final verification pass for quest system" || echo "nothing to commit"
```

---

## Self-Review Notes (for the executor)

This plan was self-reviewed against the spec. Known design decisions baked into the tasks:

- **Hybrid C** — direct interface implementation (no adapter wrappers).
- **Flat quests** — single `Target`, single `Required`, no multi-stage.
- **Shared-capable** — `MaxContributors` per-quest, per-character `Contribution` dict.
- **Auto-accept** — eligible quests auto-claim on `WorkerStartingShift`.
- **Server-authoritative mutations** — clients route via ServerRpc.
- **WorldId + MapId** — stamped at publish time; map-id used for HUD filter and save dormancy.
- **Map-transition reconciliation** — dormant snapshots wake when player returns.

Known open implementation choices (intentionally not pinned in the plan):
- The `ResolveQuest` server-side scan in `CharacterQuestLog` is O(buildings × quests). A future `QuestRegistry` singleton would make it O(1). v1 ships the scan; spec section 11 lists this as future work.
- `OriginWorldId` is currently stamped as empty string — the project's `WorldAssociation` system isn't directly accessible to a building at runtime in a clean way. Implementer should source it from whatever singleton holds the active world id (likely `GameLauncher` or `SaveManager`).
- Marker prefab visual polish is placeholder — the brainstorm picked the *style*; the prefabs ship functional but designers can iterate on materials/animations later.
