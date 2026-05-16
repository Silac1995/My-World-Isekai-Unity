# Project Rules

These rules are **mandatory** and apply to every conversation, every task, and every line of code.

> **LLM Wiki schema:** wiki content is governed by [wiki/CLAUDE.md](wiki/CLAUDE.md). Those rules only apply to operations inside `wiki/` (INGEST / QUERY / LINT / DOCUMENT-SYSTEM / SAVE / MAP). The 32 project rules below always take precedence.

## General Approach

1. This is a Unity game project — complexity is almost always higher than it first appears.
2. Before writing any code, identify all systems the change could touch or break.
3. Always think out loud before implementing: state your approach and assumptions first.
4. Never silently skip complexity with a // TODO — flag it explicitly and explain why.
5. If you are unsure how a system works in this project, ask instead of guessing.
6. Prefer the correct solution over the fast one. Speed is never the goal.
7. Always check: does this code still work correctly with 2+ Player Objects in the scene?
8. Never underestimate a task that feels simple — look for the non-obvious edge case first.

## Proactive Recommendations

9. Proactively recommend improvements to code structure, architecture, naming, or project organization whenever you spot an issue — do not wait to be asked. Flag anything that violates SOLID, creates tight coupling, or will cause maintenance problems later.

## Architecture (SOLID)

9. Each class must have one purpose — separate Health, Movement, and Data into distinct components.
10. Add features via interfaces and abstract classes, never by modifying existing logic.
11. Subclasses must be fully substitutable for their base class — no NotImplementedException in overrides.
12. Prefer many small, specific interfaces over one large general-purpose interface.
13. High-level modules must depend on abstractions (interfaces), not concrete implementations.
14. Use Dependency Injection wherever possible instead of direct class references.

## Character GameObject Hierarchy

The Character entity uses a **Facade + Child Hierarchy** pattern:
- The root `Character` GameObject holds `Character.cs`, which acts as the single facade and dependency point for all subsystems.
- Each character subsystem (e.g., `CharacterCombat`, `CharacterMovement`, `CharacterNeeds`) lives on its own **child GameObject** with only the scripts it needs. This keeps the Inspector navigable and scopes each system clearly.
- Every subsystem script holds a reference to the root `Character` component (not to other subsystems directly).
- **Cross-system communication must always go through `Character`** — a subsystem must never cache or call another subsystem directly. This enforces a single dependency graph and prevents circular coupling.
- When adding a new character system: create a child GameObject, add the script there, expose it as a `[SerializeField]` on `Character.cs`, and auto-assign it in `Awake()` via `GetComponentInChildren<>()` as a fallback.

## C# Standards

15. Always name private attributes with an underscore prefix (e.g., `_privateVariable`).
16. Always unsubscribe from events and stop or clean up coroutines in `OnDestroy`.

## Game Context

17. The game uses 2D sprites in a 3D environment — account for this in all visual and physics logic.

## Network Architecture

18. All network-related implementation must follow the full network architecture document: `NETWORK_ARCHITECTURE.md`. This document defines all rules around Server authority, Client responsibilities, Host behavior, NPC networking, Client-Side Prediction, Interest Management, Delta Compression, and the correct use of NetworkTransform vs ClientNetworkTransform. For multiplayer implementation specifics, also refer to: `.agent/skills/multiplayer/SKILL.md`. Before writing any networked logic, read the architecture doc and run the checklist at the end of it.
19. Every networked feature must be validated against **all player relationship scenarios**: Host↔Client, Client↔Client, and Host/Client↔NPC. Data that lives only on the server (static registries, runtime dictionaries) is invisible to clients — always ensure clients receive the data they need via `NetworkVariable`, ClientRpc, or `OnValueChanged` callbacks. Never assume a client has access to server-side state.
19b. **The client-side audit is mandatory before claiming any feature done — no exceptions.** Project history shows a recurring pattern where new features ship with host-only state and Kevin is the one who notices a joining client / second player can't see / can't interact / can't trigger the right pre-gate. Every feature that touches a `NetworkBehaviour` field, a state-mutating method called only from server-side paths, a UI surface that reads game state, an `InteractableObject` subclass, a `CharacterAction`, or a GOAP/BT action with state preconditions **must** run the six-question audit defined in [wiki/gotchas/host-only-state-blindspot.md](wiki/gotchas/host-only-state-blindspot.md) **before** the work is presented as complete: (1) who writes / who reads the state, (2) what replication channel is used for every client-readable field, (3) what the late-joiner sees on connect (the late-joiner repro is mandatory — host the session, mutate state, join a fresh client, verify), (4) what the client-side pre-gate reads and whether it matches the server's authoritative value, (5) what `GetComponentInParent` calls in `Awake` might fail on the spawn-race side (every one needs a `TryRegister*` late-bind fallback, see `Cashier.TryRegisterWithShop`), (6) whether proximity is gated through `InteractableObject.IsCharacterInInteractionZone` (2D X-Z only — never inline distance math). The default assumption is **"a client will read this"** — make the writer prove otherwise by grepping every consumer, not the other way around. Commits and SKILL.md updates must state, in writing, what replication channel was chosen and that the late-joiner repro was performed. Bringing an unaudited feature to a playtest is a process failure, not a code one.

## Character System

20. The Character system must be decoupled from the World/Server state. Characters must be serialized as independent local files (e.g., .json) that can be loaded into any session (Solo or Multiplayer). When in Multiplayer, all inventory and stat changes must be saved back to the player's local character file upon portal gate return or at bed checkpoints. Use `ICharacterSaveData<T>` interface to ensure each subsystem provides typed, priority-ordered serialization for the portable character profile.
21. Every time a new character subsystem is created (e.g., `CharacterCombat`, `CharacterNeeds`, `CharacterMovement`), a corresponding SKILL.md must be written in `.agent/skills/` to document its purpose, public API, events, dependencies, and integration points. This is mandatory — no character system ships without its skill file.
22. **Anything a player can do, an NPC can do, and vice versa.** All gameplay effects (placing, picking up, crafting, interacting) must go through `CharacterAction`. Player-facing systems (HUD, mouse input, ghost visuals) are UI layers that queue the same `CharacterAction` that NPC AI would queue. Never implement gameplay logic directly in a player-only manager — always route through a shared action.

## Language

23. Speak in English, write everything in English. Comments, documents, code, SKILL.md.

## MCP / Unity Editor

24. You are connected to the Unity Editor via MCP (Model Context Protocol). Use this connection to directly inspect, read, and modify the project hierarchy, components, and scripts. Always verify the actual state of the project through MCP before assuming existing logic or proposing changes.

## Rendering & Performance

25. Prioritize Shader-based solutions over CPU-bound modifications (e.g., `Image.fillAmount`, `Graphic.color`, or Sprite Vertex manipulation) for any dynamic visual feedback. Use Material Property Blocks (MPB) to ensure these changes do not break Batching. For complex color customization, prefer Palette Swapping (LUT) over global Tints to maintain artistic integrity and minimize CPU-to-GPU data transfers, especially for networked entities.

## Time & GameSpeedController

26. All time-dependent logic must explicitly account for the `GameSpeedController`. Distinguish between "Simulation Time" (gameplay mechanics) and "Real Time" (UI, menus, network heartbeats). Use `Time.deltaTime` for simulation and `Time.unscaledDeltaTime` for non-gameplay visuals. Rule: UI elements (menus, buttons, HUD animations, and "Real-Time" bars) should NOT be affected by the GameSpeedController and must use unscaled time to remain functional during pauses or high-speed intervals. For high-speed scales (Giga Speed), all tick-based simulation systems must use catch-up loops (`while` / `ticksToProcess`).

## Bug Reporting & Debugging

27. When the user reports a specific issue or bug, not only propose a fix but also identify potential "blind spots" in the logic. For every suspected cause, provide code that includes explicit `Debug.Log` or `Debug.LogError` statements at critical branching points (If/Else, Null Checks, Network Callbacks). These logs must output the internal state of variables at the exact moment of the failure.

## Skill Files, Agent & Wiki Maintenance

28. **Every** time a system is created, modified, upgraded, or refactored — not just major systems — its corresponding SKILL.md in `.agent/skills/` must be updated to reflect the changes. This includes API changes, new events, changed dependencies, removed methods, or altered behavior. If no skill exists for that system yet, create one following the template and guidelines in `.agent/skills/skill-creator/SKILL.md`. No implementation change ships without its documentation being current.

29. After completing any significant implementation (adding a system, reworking existing code, modifying cross-system behavior), **evaluate whether a specialized agent in `.claude/agents/` needs updating or creating**. Ask: (a) Does this change extend or alter the domain of an existing agent? If yes, update that agent's `.md` file to reflect the new knowledge. (b) Is this a new system complex enough (5+ interconnected scripts, cross-system dependencies, non-obvious rules) to warrant its own agent? If yes, create one. (c) If neither applies, no action is needed — do not create agents for trivial changes. Agents must always use `model: opus`.

29b. **Every** time a system is created, modified, or refactored — in addition to rules #28 (SKILL.md) and #29 (agents) — the matching page in `wiki/systems/` must be updated or created so the LLM wiki stays in sync with the code. The wiki is the source of truth for **architecture** (what the system is, why it exists, how it connects to others); SKILL.md is the source of truth for **procedure**. Do not duplicate procedural content — link to the SKILL.md in the page's `Sources` section. Required actions: (a) If a page exists, bump the `updated:` frontmatter date, append a line to `## Change log` (`- YYYY-MM-DD — <summary> — claude`), refresh `depends_on` / `depended_on_by` / `related` if cross-system relationships changed, and update any of the ten required sections affected by the change (Purpose, Responsibilities, Key classes / files, Public API, Data flow, Dependencies, State & persistence, Gotchas, Open questions, Change log). (b) If no page exists and the change qualifies as a system (owns a coherent responsibility, has multiple files, or is referenced by other systems), create it from `wiki/_templates/` following the rules in `wiki/CLAUDE.md`. (c) Trivial fixes with zero architectural impact (typos, local refactors, internal-only tweaks) may be skipped. Always read `wiki/CLAUDE.md` before touching any file under `wiki/` — it governs frontmatter, naming, wikilinks, sources, and the diff-preview rule for >5-file operations.

## World System & Simulation

30. The game uses a Living World architecture based on Map Hibernation and Macro/Micro Simulation. The world is organized as a nested hierarchy: **`Region` (authored container) → { `MapController`, `WildernessZone`, `WeatherFront` }**. All three children implement `IWorldZone`. Before implementing any system that involves NPCs, resources, buildings, time, or map state, you must account for both simulation layers:

- **Micro-Simulation** (Map active / player near zone): Real-time GOAP, NavMesh, live logistics, physical harvestables, NetworkObject presence. Runs for a `MapController` only when ≥ 1 player is present. Runs for a `WildernessZone` only for content streamed in within the player's spawn radius.
- **Macro-Simulation** (Map hibernating / no player near zone): Maps freeze, NPCs serialize into `HibernatedNPCData` and despawn. `WildernessZone`s and `WeatherFront`s never host live GameObjects when no player is near — their state exists purely as data. On wake-up / player approach, `MacroSimulator` runs a catch-up loop in order: (1) Resource Pool Regeneration, (2) Inventory Yields, (3) Needs Decay, (4) Position Snap, (5) City Growth, (6) Zone Motion (apply accumulated daily deltas from each zone's `IZoneMotionStrategy` list). No live Unity systems during hibernation — all offline progress is pure math.

**Key rules:**
- Any new NPC stat, need, or behavior that changes over time must have an offline catch-up formula in `MacroSimulator`.
- Any new resource or harvestable must be registered in `BiomeDefinition`. Runtime counts live as `ResourcePoolEntry` inside `CommunityData.ResourcePools` (map-attached) **or** `WildernessZone.Harvestables` (wilderness-attached). Never hardcode resource availability.
- Any new job type must have a `JobYieldRecipe` entry in `JobYieldRegistry`. Biome-driven jobs must set `IsBiomeDriven = true`.
- `TimeManager` is the single source of truth for all simulation math. Never use `Time.time` or `Time.deltaTime` for offline delta calculations — use `TimeManager.CurrentDay` + `CurrentTime01`.
- **Maps are never created by NPC-cluster auto-promotion.** They are born via (a) scene authoring, (b) `BuildingPlacementManager` when a player places a building outside any existing map, or (c) future procedural generation. All dynamic creation routes through `MapRegistry.CreateMapAtPosition`. **MapControllers are elastic:** new wild maps shrink-to-fit inside their Region's bounds (`MapController.ClampBoundsToRegion`) on spawn, and existing maps expand-to-envelop nearby placements (`MapController.ExpandBoundsToInclude`) when the click lands within `WorldSettingsData.MapMinSeparation`. MinSep is therefore a **soft threshold** that triggers expansion rather than hard rejection. Every building must be placed inside an authored `Region` — `BuildingPlacementManager.IsInsideRegion` gates both the client ghost and the server-authoritative spawn. Abandoned cities never release their spatial slot.
- **`WildernessZone`s** are born via (a) scene authoring, (b) `WildernessZoneManager.SpawnZone` — callable from debug tools, quest scripts, or environmental systems (e.g., a `WeatherFront` spawning a temporary berry zone), or (c) future procedural generation. They hold `List<ResourcePoolEntry>` (harvestables) and `List<HibernatedNPCData>` (wildlife, future). Contents stream via `IStreamable` only when a player is within the zone's spawn radius. Zones can move via pluggable `IZoneMotionStrategy` SO assets (default `StaticMotionStrategy`).
- **`IWorldZone`** is the shared abstraction for anything with spatial identity. Any new spatial entity type must implement it.
- Always refer to `world-system/SKILL.md` before touching any map, NPC lifecycle, or simulation logic.

## Defensive Coding & Exception Handling

31. Wrap operations that can fail at runtime (file I/O, deserialization, network callbacks, external data parsing, `GetComponent` on uncertain targets) in `try/catch` blocks. Log the exception with `Debug.LogException(e)` or `Debug.LogError` including context (what was being attempted, which object, which data). The goal is to prevent one failing subsystem from crashing the entire game — gracefully degrade instead. Do **not** swallow exceptions silently; always log them. For performance-critical paths (Update loops, tight loops), prefer null-checks and validation over try/catch.

## World Scale Reference

32. **11 Unity units = 1.67m (average human height).** Use this ratio for all spatial calculations — distances, sizes, speeds, ranges, spawn offsets, combat positions, building dimensions, furniture placement, and any other measurement. When designing or reviewing any value that represents a real-world distance, convert using this scale: **1 Unity unit ≈ 0.152m (≈15.2 cm)**.

## Player Input Ownership

33. **All player input that controls the player character lives in [PlayerController.cs](Assets/Scripts/Character/CharacterControllers/PlayerController.cs).** Keyboard (WASD, Space, Tab, C, hotkeys, …), mouse (click-to-move, click-to-target, …), and any other device input that drives the *owned player character's* movement, combat, targeting, or action queueing must be read inside `PlayerController.Update()` (gated by `IsOwner`) and routed through `CharacterAction` / `IPlayerCommand` / the appropriate `Character` subsystem. Do **not** scatter `Input.GetKey…` / `Input.GetMouseButton…` calls for player-character control across HUD scripts, UI managers, ad-hoc `MonoBehaviour`s, or other character subsystems. UI widgets may listen to UI events (`Button.onClick`, `EventSystem` focus, etc.) and may read input that targets *the UI itself* (opening menus, navigating panels, text fields), but as soon as the input result is "the player character should do X," it must be queued via `PlayerController` (e.g., `SetOrder(...)`, an action enqueue on `Character.CharacterActions`, or a method on `PlayerController`). This keeps input handling discoverable, networking-safe (single `IsOwner` gate), and consistent with rule #22 (player↔NPC parity through `CharacterAction`).

## Performance & Optimisation

34. **Optimisation is a first-class concern, not a follow-up.** This is a small, deliberately-scoped game — every frame and every byte of garbage counts, even on small scenes. When writing or reviewing any gameplay code, apply these rules. The full pattern catalogue with concrete examples lives in [wiki/concepts/performance-conventions.md](wiki/concepts/performance-conventions.md); the active deferral list in [wiki/projects/optimisation-backlog.md](wiki/projects/optimisation-backlog.md). Read both before touching any per-frame system.

  - **Per-frame allocations are bugs.** `Update()`, `LateUpdate()`, `FixedUpdate()`, BT ticks, `Job.Execute()`, GOAP `IsValid()` / `Execute()`, and coroutine `MoveNext()` paths must not allocate. Use `Physics.OverlapBoxNonAlloc` / `Physics.OverlapSphereNonAlloc` / `Physics.RaycastNonAlloc` with reused `Collider[]` / `RaycastHit[]` buffers. Cache `List<T>`, `HashSet<T>`, `Dictionary<,>` instances; clear-and-reuse, never `new` per call. **Avoid LINQ in hot paths** (`Where`, `Select`, `OfType`, `ToList`, `Any`, `FirstOrDefault`, `Sum`) — every call allocates an enumerator + the result collection. Avoid string concatenation / interpolation in hot paths unless gated.
  - **Polling is a smell — prefer event-driven or dirty-flag gating.** If a method runs every BT tick / every Update doing work that is idempotent on a stable state, it is wrong. Mark dirty on state change (item add/remove, inventory mutation, reservation flip, etc.), early-exit when clean, clear the flag at the end of a successful pass. See `LogisticsOrderBook._dispatchDirty` for the canonical pattern.
  - **Cache anything walked recursively or per-frame.** Room hierarchies, `GetComponentsInChildren`, `Physics.Overlap*` results, cross-building scans, projection getters (`x.Select(e => e.Item).ToList()`) — wrap them in a TTL or explicit-invalidation cache. **Always preserve existing fallback paths** (e.g. transform-tree scans for registration races) — pay them once per refresh, not once per query. See `CommercialBuilding.GetStorageFurnitureCached` and `CraftingBuilding.RebuildCraftableCacheIfStale` for the canonical pattern.
  - **Centralise cache invalidation through chokepoint methods, not callsite-by-callsite.** When a cache depends on furniture / inventory / room state, hook the invalidation into the lifecycle method everyone goes through (e.g. `FurnitureManager.InvalidateOwnerBuildingCaches` is called from every register/unregister method, not from each action). Less surface to forget.
  - **Gate every `Debug.Log` in a hot path.** Wrap with `if (NPCDebug.VerboseJobs)` / `VerboseActions` / equivalent toggle, or move it behind `#if UNITY_EDITOR` / `Debug.isDebugBuild`. An ungated log triggers `StackTraceUtility.ExtractStackTrace` (~4 KB / call) and on Windows can cause the documented host-progressive-freeze pattern (`wiki/gotchas/host-progressive-freeze-debug-log-spam.md`). This applies even inside `Awake`/`OnEnable` — frequent activations make those count as "hot path" too.
  - **Debug UI / instrumentation must be dev-mode-only or properly throttled.** A debug overlay running in production scenes is the single most common cause of unexpected frame-budget loss — measured at 28% of frame in our 2026-04-27 profiler session for `UI_CommercialBuildingDebugScript`. Gate behind `DevMode` / dev-build / a single shared toggle. If it must run in production, stagger updates (one building per frame, or 1 Hz), cull off-screen instances, and profile-verify the cost.
  - **Throttle heavy per-tick logic with a per-job / per-component cadence interval, not a global tick rate change.** When a system runs at the BT/Update tick rate but its work is reactive to slower-changing state, expose an `ExecuteIntervalSeconds` (or equivalent) and gate the heavy call. Don't slow the BT itself — combat reaction and schedule transitions still need 10 Hz. See `Job.ExecuteIntervalSeconds` + `BTAction_Work.HandleWorking` for the canonical pattern.
  - **Pool gameobjects/prefabs that get repeatedly Instantiated/Destroyed.** Every `Instantiate(prefab)` of a multi-renderer prefab allocates ~hundreds of KB. For things created per gameplay loop (`WorldItem`, VFX, damage numbers, etc.), reuse instances. **Network-aware pooling** must respect NGO's spawn lifecycle (see [[network-architecture]]).
  - **When in doubt, profile.** Unity Profiler in Deep Profile + Allocation Tracking mode is the only honest source of truth. **Don't pre-commit to a fix without measuring first.** GC.Alloc per frame matters at least as much as Self ms — spike frames almost always come from major GC, and major GC frequency = (alloc/sec) / (GC threshold). For multi-step optimisation work, measure before/after each step, not just at the end. Note: the Editor adds ~15% overhead and inflates `LogStringToConsole` cost — capture standalone Mono builds for the truth.
  - **Network safety holds for every cache and every flag.** All optimisation state (dirty flags, TTL caches, reused buffers) must live on the server side or be per-peer (each client maintains its own). Never put per-frame caches on `NetworkBehaviour` fields without explicit `[ServerOnly]` reasoning. See rule #18 + #19.
  - **Optimisation tracking lives in the wiki, not in source TODOs or memory.** Active deferrals go to [wiki/projects/optimisation-backlog.md](wiki/projects/optimisation-backlog.md) with a profiler-measured cost, an owner, and a "good enough" threshold. Do not leave `// TODO: optimize` comments in code.

## ECS / DOTS Adoption Gate

35. **Unity ECS / Entities is installed (`com.unity.feature.ecs@1.0.0`) but the project is GameObject-based.** For any new system, the **default is MonoBehaviour** — ECS is the exception, not the baseline. Before choosing the shape of a new system, apply the decision gate at [wiki/concepts/unity-ecs.md](wiki/concepts/unity-ecs.md) (full reference: [wiki/references/unity-ecs-manual.md](wiki/references/unity-ecs-manual.md), local mirror at `raw/articles/unity-ecs-manual/`). ECS qualifies *only* when ALL of these hold: (a) iterating ~1k+ entities per frame or per tick, (b) no NGO replication required (or pure server-side simulation), (c) no Inspector / prefab / `ICharacterVisual` / Spine binding, (d) no Character / Building / Item / Network entanglement, (e) profiler-confirmed bottleneck *or* projected scale where plain `[BurstCompile]` + `NativeArray<T>` cannot keep up. The middle path — **Burst + Jobs without ECS** (`[BurstCompile] IJob` / `IJobParallelFor` over `NativeArray<T>`) — handles most "this is too slow" situations and **must be tried before reaching for ECS**. Permanent non-fits: Character, Buildings, Items, UI, dialogue, combat actions, character orders, anything Inspector-authored or NGO-replicated. Half-and-half hybrid pipelines cost more than they save — when in doubt, stay GameObject.

## Interactable Proximity (NPC ↔ Interactable)

36. **NPC "am I close enough to interact?" is `InteractableObject.IsCharacterInInteractionZone(worker)` — NEVER raw `Vector3.Distance(worker, GetInteractionPosition(...)) < N`.** This applies to every `GoapAction.Execute`, every `CharacterAction.OnStart` / `OnTick`, every `BTAction` movement gate, and every `IPlayerCommand` that walks a character to a target before doing something to it. The naive `Vector3.Distance` check is a load-bearing bug, not a shortcut: `CharacterMovement.SetDestination` internally calls `NavMesh.SamplePosition(target, 5m, …)`, which routinely lands the agent **several metres off the requested interaction point** because the interactable's own collider blocks the NavMesh directly under it (cashier counter, chest, crafting station, bed, door, etc.). The agent stops at the sampled landing point — but the gate is still measuring against the original off-mesh dest, so the distance never falls below the threshold, `_isMoving` stays `true`, `SetDestination` is never re-issued, and the NPC stands frozen forever in front of the object it wanted to use. The user-visible symptom is "NPC walks up, then just stares at the cashier / chest / crafting station." This bit us on `GoapAction_BuyFood` on 2026-05-15; the same shape lurks in any new action that copies the simple-distance pattern.

   **Canonical gate (mirror this verbatim — see `GoapAction_FetchSeed`, `GoapAction_ReturnToolToStorage`, `GoapAction_FetchToolFromStorage`, `GoapAction_BuyFood`, `GoapAction_GoShopping`, `GoapAction_GatherStorageItems`, `GoapAction_TakeFromSourceFurniture`):**

   ```csharp
   var interactable = target.GetComponent<InteractableObject>(); // e.g. CashierInteractable, FurnitureInteractable
   bool inZone;
   if (interactable != null && interactable.InteractionZone != null)
   {
       inZone = interactable.IsCharacterInInteractionZone(worker);
       if (!inZone)
       {
           // Softlock guard: path landed just outside the zone (SamplePosition pulled
           // the landing off the in-mesh interaction point). Without this, _isMoving
           // stays true forever and the worker never re-SetsDestination → frozen NPC.
           bool arrived = !movement.HasPath
               || movement.RemainingDistance <= movement.StoppingDistance + 0.5f;
           if (arrived)
           {
               Vector3 ip = target.GetInteractionPosition(worker.transform.position);
               Vector3 wp = worker.transform.position;
               if (Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                                    new Vector3(ip.x, 0f, ip.z)) <= 2f) inZone = true;
           }
       }
   }
   else
   {
       // No InteractionZone collider on this target → fall back to flat-XZ distance.
       Vector3 ip = target.GetInteractionPosition(worker.transform.position);
       Vector3 wp = worker.transform.position;
       inZone = Vector3.Distance(new Vector3(wp.x, 0f, wp.z),
                                 new Vector3(ip.x, 0f, ip.z)) <= 1.5f;
   }

   if (!inZone)
   {
       // Re-fire SetDestination when the agent dropped its path. The sticky `_isMoving`
       // flag alone is not enough: the BT can switch away from this branch (e.g. a
       // NeedHunger-driven GoapAction_BuyFood plan preempts the work branch) and on
       // return, the agent's destination has been cleared while `_isMoving` is still
       // true. Without the `|| !movement.HasPath` re-fire, the NPC stands frozen
       // forever in the "en route" state with no actual path. Same shape lives on
       // JobVendor.Execute branch 3 (vendor walking to a cashier).
       if (!_isMoving || !movement.HasPath)
       {
           movement.SetDestination(target.GetInteractionPosition(worker.transform.position));
           _isMoving = true;
       }
       return;
   }
   // arrived: enqueue the CharacterAction / fire the interaction.
   ```

   **When designing a new interactable** (`Cashier`-shaped, `Furniture`-shaped, `WorldItem`-shaped, anything an NPC walks up to): the `InteractionZone` collider on the matching `InteractableObject` is the authoritative "you can interact from here" gate. Author it generously — a few times the agent's stopping distance is normal — so the NavMesh-sampled landing point always falls inside it. The "interaction point" returned by `GetInteractionPosition` is a *navigation hint*, not the arrival truth; if the NavMesh can't reach it, the zone-overlap test must still succeed.

   **Companion rule (anti-spam):** wrap movement-gate `Debug.Log`s behind `NPCDebug.VerboseActions` (or equivalent) — every BT tick re-evaluates the gate, and an ungated log line per tick per NPC is the documented progressive-freeze pattern (rule #34).

   **Reminder for every new NPC↔interactable behaviour:** before pressing Save, walk through this checklist:
   1. Does the target expose `InteractableObject.InteractionZone`? If yes, gate on `IsCharacterInInteractionZone`. If no, add one — don't paper over with `Vector3.Distance`.
   2. Is there a softlock guard for "path landed just outside the zone"? Mirror the `HasPath`/`RemainingDistance` block above.
   3. Is the "still walking" branch resilient to **path-loss** (BT branch switch / knockback / brief `OccupyingFurniture` / transient NavMesh exit)? Sticky flags like `_isMoving` / `_isMovingToCashier` must be paired with a `!movement.HasPath` re-fire of `SetDestination`, otherwise the worker freezes in the en-route state forever — this is what bit `JobVendor` on 2026-05-15 the moment `NeedHunger` started preempting the work branch.
   4. Is the movement gate the only proximity check? `CharacterActions.ExecuteAction` already re-validates `IsCharacterInInteractionZone` server-side as anti-cheat — your gate must agree with the server's gate, or the action will be queued and immediately rejected.
   5. If this action is also reachable by a player command (rule #33), the same zone is used by the `Interact` path on the `Interactable` — keep them symmetric so the NPC and the human see the same "can interact" surface.

## Standalone Build Crash Diagnostics

37. **For any native crash in the standalone player (`Crash!!!` in `Player.log`, no managed exception, addresses inside `UnityPlayer.dll`), copying `UnityPlayer_Win64_player_development_mono_x64.pdb` from the Unity install to the build folder is the FIRST diagnostic step, not the tenth.** Without managed function names in the stack trace, every other diagnostic step is guessing. The cost is 30 seconds; the savings are hours. See [wiki/gotchas/material-buildproperties-standalone-crash.md](wiki/gotchas/material-buildproperties-standalone-crash.md) for the May 2026 incident this rule was extracted from.

    **Setup (Development Build, Mono Standalone):**
    ```powershell
    $pdbSrc = "C:\Program Files\Unity\Hub\Editor\<UNITY_VERSION>\Editor\Data\PlaybackEngines\WindowsStandaloneSupport\Variations\win64_player_development_mono\UnityPlayer_Win64_player_development_mono_x64.pdb"
    Copy-Item $pdbSrc -Destination "<path to build folder containing UnityPlayer.dll>"
    # Also copy UnityCrashHandler64.pdb from the same Variations folder.
    ```
    **Keep the embedded PDB filename** (`UnityPlayer_Win64_player_development_mono_x64.pdb`, NOT renamed to `UnityPlayer.pdb`) — dbghelp matches by GUID embedded in the DLL's debug-directory record, but the filename has to match what the DLL points to. Renaming defeats it.

    **Build Settings:** ☑ Development Build, ☑ Script Debugging (Mono debugger hooks present so dbghelp can correlate). Set programmatically via `EditorUserBuildSettings.development = true; EditorUserBuildSettings.allowDebugging = true;` if you don't want to click. For Release builds, use `WindowsPlayer_player_Release_mono_x64.pdb` from the same `Variations/` folder.

    **Re-run the existing build (no rebuild needed)** — dbghelp loads the PDB at crash time. The next `OUTPUTTING STACK TRACE` section will have managed function names like `ShaderPropertySheet::UpdateTextureInfo` instead of `(function-name not available)`.

## Editor vs Build Serialization Tolerance

38. **The Unity Editor's asset loader is lenient; the standalone Mono build's native loader is strict.** Any asset that the Editor "loads with a warning" can crash the standalone build natively at scene load with no managed exception. Categories that have bitten this project:

    - **Materials with broken/dangling texture references** (`_MainTex` GUID points to a deleted texture) — Editor renders magenta + warns, build crashes in `Material::BuildProperties → UpdateTextureInfo`.
    - **Materials with `m_InvalidKeywords`** (the material has a keyword the shader no longer declares) — Editor strips silently, build can crash on property-sheet build.
    - **Materials with `Infinity` values in serialized Color/Vector properties** (e.g. `_CameraFadeParams: {r: 0, g: Infinity, b: 0, a: 0}`) — Editor handles, Mono build's native binary deserializer can choke on it.
    - **ScriptableObjects in `Resources/` with `m_Script: {fileID: 0}` or a script GUID that doesn't exist** — Editor warns "Script attached to '...' is missing or no valid script is attached," build's Resources/ preload at startup can crash.
    - **Prefabs with `MonoBehaviour` components whose script GUID no longer resolves to a `.cs` file** — Editor logs "The referenced script on this Behaviour is missing," build can crash on `AwakeFromLoadQueue`.
    - **`[SerializeReference]` polymorphic fields holding a type that was renamed/deleted** — Editor shows `Missing managed reference`, build deref crashes on first access.
    - **`CharacterProfileSaveData.partyMembers`-style self-referential type cycle** without `[NonSerialized]` — Editor logs "Serialization depth limit 10 exceeded," build chains warnings into a native crash during scene deserialization.

    **Implications:**
    - **A clean Editor Console does NOT mean a healthy build.** Every "warning" the Editor logs about asset deserialization is a potential build crash. Treat them as build-blocking until proven otherwise.
    - **The Editor's `AssetDatabase` resolves references on demand and falls back to null; the build's pre-baked fileID/GUID tables don't have a fallback path.** If a reference can't resolve, the build either skips it (best case) or segfaults (worst case).
    - **Resources/ folder content is pre-loaded en masse at app start in the build** (not the Editor). Any single corrupt asset in `Resources/` is a boot-time crash. Scan `Resources/` after any asset deletion or script rename.
    - **OnValidate doesn't run in the build.** Anything the Editor "auto-heals" via OnValidate (clamps, defaults, missing-ref repair) flows straight into Awake unmodified in the standalone player.

    **Before shipping or after any asset/script rename or deletion, run the broken-reference scan in this Roslyn snippet** (drop into a temporary Editor script):
    ```csharp
    foreach (var p in Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories)) {
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p.Replace('\\','/'));
        if (obj == null) Debug.LogError($"[ASSET_NULL_LOAD] {p}");
        else if (obj is ScriptableObject so && MonoScript.FromScriptableObject(so)?.GetClass() == null)
            Debug.LogError($"[ASSET_MISSING_TYPE] {p}");
    }
    // Mirror this scan for *.prefab to catch missing-script MonoBehaviours, and walk
    // SerializedObject iterators on every material's m_SavedProperties.m_TexEnvs to
    // catch broken texture refs (objectReferenceValue == null && objectReferenceInstanceIDValue != 0).
    ```
    Pair this scan with the `UnityPlayer.pdb` setup in rule #37 so the next standalone crash arrives with a managed-name stack trace ready to read.

## UI HUD Prefab Architecture

39. **Any UI window / surface that can be closed MUST be a Prefab Variant of [Assets/UI/Player HUD/UI_WindowBase.prefab](Assets/UI/Player%20HUD/UI_WindowBase.prefab) and live under `Assets/UI/Player HUD/`.** The defining trait is **"has a close affordance"** — not size or modality. Full-screen panels, half-screen modals, side drawers, mid-screen popups, confirmation dialogs, tooltips that the user can dismiss, sub-windows opened from inside another window — all of them MUST inherit `UI_WindowBase`. The only UI prefabs that are *not* subject to this rule are leaf elements that have no close button and live as `Instantiate`d children inside their parent window (rows, tiles, list items, badges).

    The base prefab supplies the standard window chrome (Canvas in **`RenderMode.ScreenSpaceCamera`** + GraphicRaycaster + CanvasScaler set to `ScaleWithScreenSize` @ 1920×1080 + `Panel_Main_Background` Image + the inherited `_buttonClose` Button wiring). Variants inherit that chrome and add their own content on top — no window re-implements the canvas/raycaster/background trio, no window re-wires its own close-button listener, and **no window changes the Canvas renderMode** (every closable window is `ScreenSpaceCamera`, period — not Overlay, not WorldSpace). The `worldCamera` field is left null on the prefab asset (prefabs can't reference scene cameras) and assigned at runtime via `UI_WindowBase.Awake` to `Camera.main`.

    **Concretely:**
    - **Asset location**: new variant goes in `Assets/UI/Player HUD/` (alongside `UI_StorageFurniturePanel.prefab`, `UI_SafePanel.prefab`, etc.). Never put closable-window variants in `Assets/Prefabs/` or any other UI folder.
    - **Prefab Variant relationship**: when you author the new window, base it on `UI_WindowBase.prefab` (right-click in Project view → Create → Prefab Variant, or via `PrefabUtility.InstantiatePrefab(baseWindowAsset)` + `PrefabUtility.SaveAsPrefabAsset`). Verify after authoring: `PrefabUtility.GetCorrespondingObjectFromSource(savedAsset).name == "UI_WindowBase"`.
    - **Backing script must inherit [UI_WindowBase](Assets/Scripts/UI/UI_WindowBase.cs)**. The base `Awake` (a) auto-wires the inherited `_buttonClose` Button, (b) walks the variant's Canvas tree (root + children) and assigns `Camera.main` to any `ScreenSpaceCamera` canvas whose `worldCamera` is still null. The base exposes `OpenWindow` / `CloseWindow` (the canonical activation pair). When you override `Awake` in a variant, **call `base.Awake()` first** — otherwise the close button and worldCamera lookup don't run and the window won't render. Override `CloseWindow` to add cleanup (unsubscribe events, clear rows, etc.) and call `base.CloseWindow()` last.
    - **Don't add a Canvas / GraphicRaycaster / CanvasScaler in your variant's `Awake`.** The inherited Canvas child (from the base prefab) already supplies them. Adding a second Canvas on the variant root produces conflicting render-mode / sorting state at runtime and was the root cause of the 2026-05-16 "visible in Scene view but invisible in Game view" symptom. If the variant's content needs to live somewhere, parent it under the inherited Canvas child, not under the variant root.
    - **Wire the inherited `_buttonClose`** to the close button on your variant. Do this once at prefab-authoring time. Without it, the close button does nothing.
    - **Singleton entry-point lives on [PlayerUI.cs](Assets/Scripts/UI/PlayerUI.cs)**. Expose `Open<Name>Window(... contextual args ...)` and `Close<Name>Window()` as the only public surface — every caller (`Furniture.OnInteract`, `CharacterAction` ClientRpcs, hold-E menu options, parent-window code that opens a sub-window) routes through these. The window itself is a `[SerializeField] private UI_<Name>Window _xxxWindow;` field on `PlayerUI`, assigned in the scene under the `UI_PlayerHUD` GameObject (a scene-resident child of PlayerUI, never instantiated at runtime).
    - **Open<Name>Window must `Debug.LogWarning` when `_xxxWindow` is null** so the diagnostic surfaces in the Console when a designer forgets to wire the SerializeField. Pattern: `if (_xxxWindow == null) { Debug.LogWarning("<color=orange>[PlayerUI]</color> Open<X>Window called but _<x>Window SerializeField is null — author the prefab (variant of UI_WindowBase.prefab) and wire it to PlayerUI._<x>Window in the Inspector."); return; }`. Saves hours of "why doesn't anything happen?" debugging.
    - **Sub-windows opened from inside another window** follow the same rule — they are Variants of `UI_WindowBase.prefab`, they live in `Assets/UI/Player HUD/`, and the parent window opens them via `PlayerUI.Instance.Open<Name>Window(...)` (not by holding a direct child reference). Keep the façade flat: every closable window is a sibling under PlayerUI, never a grand-child of another window.
    - **Non-window UI elements (rows, tiles, list items, badges, tooltips that auto-fade without user dismissal) are NOT subject to this rule** — they are leaf UI prefabs (e.g. `UI_SafeCurrencyRow.prefab`, `UI_ShopBuyRow.prefab`) and are stored alongside their parent window. The parent window holds a reference to the row prefab via its own `[SerializeField] private UI_<Name>Row _rowPrefab` field and `Instantiate`s rows at runtime under a `_rowContainer` RectTransform. **Litmus test**: if the element has a Button that calls `CloseWindow` / `SetActive(false)` to dismiss itself, it is a window and the rule applies; if it disappears only when its parent window closes or via a timer, it is a leaf and the rule does not apply.
    - **Visual styling** (fonts, sprites, layout group tuning) is a separate authoring pass that can happen after the functional scaffold ships. A scaffold authored via MCP / Roslyn with placeholder visuals is acceptable for development; visual polish lands later. Do not gate functional work on pixel-perfect styling.

    See [wiki/systems/player-hud.md](wiki/systems/player-hud.md) for the architecture page, [.agent/skills/ui-hud/SKILL.md](.agent/skills/ui-hud/SKILL.md) for the procedural skill, and the `ui-hud-specialist` agent in `.claude/agents/` for the deep-dive specialist. Spawn that agent whenever you create / modify / debug any closable window.
