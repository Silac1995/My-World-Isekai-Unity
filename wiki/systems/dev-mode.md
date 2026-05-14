---
type: system
title: "Dev Mode"
tags: [debug, host-only, dev-tools, tier-2]
created: 2026-04-21
updated: 2026-05-14
sources: []
related:
  - "[[engine-plumbing]]"
  - "[[commercial-building]]"
  - "[[building]]"
  - "[[character]]"
  - "[[character-combat]]"
  - "[[character-skills]]"
  - "[[character-needs]]"
  - "[[character-wallet]]"
  - "[[character-work-log]]"
  - "[[player-ui]]"
  - "[[network]]"
  - "[[farming]]"
  - "[[terrain-and-weather]]"
  - "[[game-speed-controller]]"
  - "[[runtime-uichildren-collapse-under-prefab-vlg]]"
  - "[[kevin]]"
status: stable
confidence: high
primary_agent: debug-tools-architect
secondary_agents: []
owner_code_path: "Assets/Scripts/Debug/DevMode/"
depends_on:
  - "[[engine-plumbing]]"
  - "[[commercial-building]]"
  - "[[building]]"
  - "[[character]]"
  - "[[character-combat]]"
  - "[[character-skills]]"
  - "[[character-needs]]"
  - "[[character-wallet]]"
  - "[[character-work-log]]"
  - "[[network]]"
  - "[[farming]]"
  - "[[terrain-and-weather]]"
  - "[[game-speed-controller]]"
depended_on_by: []
---

# Dev Mode

## Summary
Host-only developer/god-mode overlay that layers a togglable admin panel and input-gate on top of the normal gameplay loop. Activated via `F3` in editor/dev builds or `/devmode on` in release builds; clients never see it take effect. Ships four modules — **Spawn** (click-to-spawn fully configured NPCs), **Select** (click-to-select a Character and run `IDevAction` plug-ins), **Inspect** (read-only runtime inspection of the selected `InteractableObject`, with a 10-tab [[character]] inspector + a storage-furniture view that lists every slot of a chest/shelf/barrel/wardrobe), and **Game Speed** (preset + free-form Time.timeScale, routed through the networked [[game-speed-controller|GameSpeedController]]). Input gating keeps WASD movement live (at god-mode speed) while suppressing right-click move, TAB target, Space attack, and E interact. For the procedural how-to (adding modules, adding actions, adding inspector views and sub-tabs), follow [.agent/skills/debug-tools/SKILL.md](../../.agent/skills/debug-tools/SKILL.md) and [.agent/skills/dev-mode/SKILL.md](../../.agent/skills/dev-mode/SKILL.md).

## Purpose
Developers and hosts need to iterate on content and reproduce bugs without restarting sessions: spawn NPCs with hand-picked personalities, traits, combat styles, and skills; click to assign a Character as owner of a building; eventually grant items, teleport, pause sim, and edit live state. Before this system existed, the only options were scripted spawn buttons in [[engine-plumbing|debug-script]] (flat UI, no config), save-file editing, or full session restarts. Dev Mode consolidates these into a single host-authoritative tool with a plug-in contract so new modules can be added without touching the core.

Release safety is explicit: the whole system is locked behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` for default unlock, and in release builds the host must explicitly type `/devmode on` in chat once before `F3` does anything. Clients cannot unlock or enable it at all.

## Responsibilities
- Owns the activation gate (`IsUnlocked` session-level, `IsEnabled` live state) and the `OnDevModeChanged` event.
- Owns a single-slot click arbitration system (`ActiveClickConsumer` + `OnClickConsumerChanged`) so only one module consumes mouse clicks at a time.
- Owns the static `SuppressPlayerInput` read used by the player controller and interaction detector to gate gameplay actions while the panel is open.
- Owns the `GodModeMovementSpeed` constant consumed by [[character-movement|PlayerController]] when dev mode is active.
- Hosts a tab-based panel prefab (`DevModePanel`) lazy-loaded from `Resources/UI/DevModePanel`; modules live as child GameObjects and self-register by subscribing to `OnDevModeChanged`.
- Routes `/`-prefixed chat input through `DevChatCommands.Handle` and rejects non-host callers with a warning.
- Provides the `IDevAction` plug-in contract for Select-tab actions (`Label`, `IsAvailable`, `Execute`).
- Provides read-only runtime inspection of the selected `InteractableObject` (Inspect tab) via the `IInspectorView` dispatch contract and `CharacterInspectorView` with 10 sub-tabs.

**Non-responsibilities** (common misconceptions):
- Not responsible for save/load — dev-mode state is runtime-only, never serialized. Persistence of spawned NPCs is handled by [[save-load]] the same way any other runtime NPC is persisted.
- Not responsible for replicating spawn-config extras (behavioral traits, combat styles) to late-joining clients — see Known gotchas below. The server applies values; broader replication is handled by [[network]] and subsystem-specific `NetworkVariable` / `NetworkList` channels (see [[character-skills]] for the one that already works).
- Not responsible for gameplay — click-to-spawn uses [[engine-plumbing|spawn-manager]] and `SetOwner` APIs already exposed by [[commercial-building]] / [[building]]. Dev Mode is a UI + arbitration layer on top; no gameplay logic lives here.
- Not responsible for client-side cheats — it is explicitly host-only. Giving clients any of these powers needs a separate design pass for trust and replication.

## Key classes / files

| File | Role |
|------|------|
| [DevModeManager.cs](../../Assets/Scripts/Debug/DevMode/DevModeManager.cs) | Singleton. Owns `IsUnlocked`, `IsEnabled`, `SuppressPlayerInput` static, `GodModeMovementSpeed` const, click-consumer slot, `F3` input, `OnDevModeChanged` + `OnClickConsumerChanged` events. **Also owns the global shortcut layer** (`HandleGlobalShortcuts` — Ctrl+Click interior-select / Alt+Click building-select / Space+LMB spawn / ESC cancel) because tab content GameObjects are deactivated off-tab, so shortcut logic can't live on tab modules. Caches `DevSelectionModule` + `DevSpawnModule` refs (via `GetComponentInChildren(true)`) after `EnsurePanel`. |
| [DevModePanel.cs](../../Assets/Scripts/Debug/DevMode/DevModePanel.cs) | Panel root. Tab registry (`TabEntry` struct + `SwitchTab(int)`) + `ContentRoot` hosting module children. |
| [DevChatCommands.cs](../../Assets/Scripts/Debug/DevMode/DevChatCommands.cs) | Static `Handle(rawInput)` parsing `/devmode on\|off`. Host-only; clients get a warning. |
| [DevSpawnModule.cs](../../Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs) | Spawn tab. Configures race/prefab/personality/trait/combat-styles/skills and click-spawns on the `Environment` layer. Public shortcut entry: `TrySpawnAtCursor()`. |
| [DevInspectTabBuilder.cs](../../Assets/Editor/DevMode/DevInspectTabBuilder.cs) | Editor-only one-shot utility. `[MenuItem("Tools/DevMode/Build Inspect Tab")]` programmatically builds the Inspect tab hierarchy in the DevModePanel prefab and wires every serialized reference. Idempotent + destructive-rebuild variants. |
| [DevSpawnRow.cs](../../Assets/Scripts/Debug/DevMode/Modules/DevSpawnRow.cs) | Reusable multi-entry row (dropdown + level + remove button) for combat styles and skills. |
| [DevSelectionModule.cs](../../Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs) | Select tab. Generalized from Character-only to `InteractableObject`. Exposes `SelectedInteractable` + `OnInteractableSelectionChanged` (new) and `SelectedCharacter` + `OnSelectionChanged` (back-compat). Two raycast masks: `_selectableLayerMask` (interior — Ctrl+Click, default `RigidBody + Furniture`; field `_characterLayerMask` renamed with `[FormerlySerializedAs]`) and `_buildingLayerMask` (Alt+Click, default `Building`). Public shortcut entries: `TrySelectAtCursor(out label)`, `TrySelectBuildingAtCursor(out label)`, `ClearSelection()`, `DisarmToggle()`, `IsArmed`. |
| [IDevAction.cs](../../Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs) | Plug-in interface for Select-tab actions (`Label`, `IsAvailable`, `Execute`). |
| [DevActionAssignBuilding.cs](../../Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs) | First action. Claims the click slot and dispatches polymorphically to `CommercialBuilding.SetOwner` or `ResidentialBuilding.SetOwner`. |
| [IInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/IInspectorView.cs) | Dispatch contract for Inspect tab: `CanInspect(InteractableObject)`, `SetTarget(InteractableObject)`, `Clear()`. |
| [DevInspectModule.cs](../../Assets/Scripts/Debug/DevMode/Inspect/DevInspectModule.cs) | Inspect tab root. Auto-discovers `IInspectorView` children at `Awake`, activates first matching view on selection change, shows placeholder when none match. |
| [CharacterInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/CharacterInspectorView.cs) | `IInspectorView` for `CharacterInteractable`. Owns 10 `SubTabEntry` references; `Update()` refreshes only the active sub-tab. |
| [CharacterAIDebugFormatter.cs](../../Assets/Scripts/Debug/DevMode/Inspect/CharacterAIDebugFormatter.cs) | Static helpers for AI debug strings. Shared by `UI_CharacterDebugScript` and `AISubTab`. `FormatAll(Character)` composes all helpers. |
| [CharacterSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CharacterSubTab.cs) | Abstract base. `Refresh(Character)` wraps `RenderContent` in try/catch; `Clear()` resets TMP. |
| [IdentitySubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/IdentitySubTab.cs) | Name / Gender / Age / Race / Archetype / CharacterId / OriginWorld + state flags. |
| [StatsSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/StatsSubTab.cs) | CharacterCombatLevel + all 18 CharacterStats fields. |
| [SkillsTraitsSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SkillsTraitsSubTab.cs) | CharacterTraits personality + CharacterSkills.Skills. |
| [NeedsSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/NeedsSubTab.cs) | CharacterNeeds.AllNeeds with urgency + color coding. |
| [AISubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/AISubTab.cs) | Delegates to `CharacterAIDebugFormatter.FormatAll`. |
| [CombatSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CombatSubTab.cs) | CharacterCombat state + CharacterStatusManager.ActiveEffects. |
| [SocialSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/SocialSubTab.cs) | CharacterRelation.Relationships + CharacterCommunity + CharacterMentorship. |
| [EconomySubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/EconomySubTab.cs) | CharacterWallet.GetAllBalances() + CharacterJob + CharacterWorkLog.GetAllHistory(). |
| [KnowledgeSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/KnowledgeSubTab.cs) | CharacterBookKnowledge + CharacterSchedule (ToString() placeholders; follow-up). |
| [InventorySubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/InventorySubTab.cs) | CharacterEquipment (ToString() placeholder; follow-up). |
| [StorageFurnitureInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/StorageFurnitureInspectorView.cs) | `IInspectorView` for `FurnitureInteractable` whose `Furniture` is a `StorageFurniture`. Renders a header + per-frame slot listing (capacity / locked / full + `[index] <SlotType> — <item>` lines). Read-only, no inventory mutation. |
| [DevStorageFurnitureInspectorBuilder.cs](../../Assets/Editor/DevMode/DevStorageFurnitureInspectorBuilder.cs) | Editor-only one-shot. `[MenuItem("Tools/DevMode/Build Storage Furniture Inspector")]` adds the storage view GO under `InspectContent/Views`, sibling to `CharacterInspectorView`, and wires `_headerLabel` + `_content`. Idempotent + destructive-rebuild variants. |
| [HarvestableInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/HarvestableInspectorView.cs) | `IInspectorView` for any `Harvestable` (wilderness trees / rocks / ore + farmed crops). Renders Identity (type / GO name / layer / position / NetworkObjectId), Harvestable state (category / depleted / remaining yield / harvest tool / outputs), Destruction (when allowed), and a Crop section when the target is a `CropHarvestable` (CropSO id + display name, stage / mature flag, perennial / regrow days, full `TerrainCell` readout — moisture / fertility / plowed / growth timer / time-since-watered). Read-only. Pulls the live network state from the sibling `CropHarvestableNetSync` so values stay in sync with what the host writes. |
| [DevHarvestableInspectorBuilder.cs](../../Assets/Editor/DevMode/DevHarvestableInspectorBuilder.cs) | Editor-only one-shot. `[MenuItem("Tools/DevMode/Build Harvestable Inspector")]` adds the harvestable view GO under `InspectContent/Views`, sibling to the existing inspector views, and wires `_headerLabel` + `_content`. Idempotent + destructive-rebuild variants. |
| [UI_CharacterDebugScript.cs](../../Assets/Scripts/UI/WorldUI/UI_CharacterDebugScript.cs) | Legacy per-entity overlay. Formatting logic replaced with `CharacterAIDebugFormatter` calls (zero behaviour change). |
| `Assets/Resources/UI/DevModePanel.prefab` | Panel prefab (tab buttons + `ContentRoot` + module children). |
| `Assets/Resources/UI/DevSpawnRow.prefab` | Row prefab for combat-style / skill entries. |

## Public API / entry points

- `DevModeManager.Instance` — singleton, host-only.
- `DevModeManager.SuppressPlayerInput` (static bool) — global read used by [[character-movement|PlayerController]] and [[engine-plumbing|PlayerInteractionDetector]] + [[engine-plumbing|camera-follow]] to gate input. Static so hot-path Updates don't dereference `Instance` every frame.
- `DevModeManager.GodModeMovementSpeed` (static const float, `17f`) — WASD speed while dev mode is active.
- `DevModeManager.OnDevModeChanged : Action<bool>` — modules subscribe to show/hide their UI.
- `DevModeManager.ActiveClickConsumer` (MonoBehaviour) + `OnClickConsumerChanged` event + `SetClickConsumer(x)` / `ClearClickConsumer(x)` — single-slot click arbitration so Spawn and Select (and any future armed module) don't fight over clicks.
- `DevModeManager.Unlock() / Lock() / TryEnable() / Disable() / TryToggle()` — session and panel state transitions.
- `DevChatCommands.Handle(string rawInput)` — entry point from [[player-ui|UI_ChatBar]] for `/`-prefixed input.
- `IDevAction` interface — plug-in contract for Select-tab actions. Action is a MonoBehaviour under `SelectTab/ActionsContainer`.

**DevSelectionModule (additive surface — Select tab + Inspect tab):**
- `SelectedInteractable : InteractableObject` — current interactable (any type).
- `OnInteractableSelectionChanged : Action<InteractableObject>` — fires on any change. Subscribed by `DevInspectModule`.
- `SetSelectedInteractable(InteractableObject)` — replaces selection.
- `TrySelectAtCursor(out string label)` — interior raycast (`_selectableLayerMask`, default `RigidBody + Furniture`). Used by Ctrl+Click and the armed Select toggle.
- `TrySelectBuildingAtCursor(out string label)` — building raycast (`_buildingLayerMask`, default `Building`). Used by Alt+Click. Special-cases `BuildingInteractable` (added 2026-05-06 by the cooperative construction loop): when the raycast resolves an `InteractableObject` that is a `BuildingInteractable`, walk up to its `Building` and route through `SetSelectedBuilding` instead of `SetSelectedInteractable`. Without this branch, Alt+Click on a building shell would fill the wrong selection slot and the Inspect tab would never find a matching `IInspectorView`.
- `SelectedCharacter`, `OnSelectionChanged`, `SetSelectedCharacter` — back-compat paths preserved for existing `IDevAction` consumers.

**Inspect tab — `IInspectorView` contract:**
- `bool CanInspect(InteractableObject target)` — returns true if this view handles the target type.
- `void SetTarget(InteractableObject target)` — activates and populates the view.
- `void Clear()` — resets to placeholder state.

**Character sub-tabs — `CharacterSubTab`:**
- `void Refresh(Character c)` — public entry point; wraps `RenderContent` in try/catch.
- `void Clear()` — virtual; resets TMP_Text to placeholder.
- `protected abstract string RenderContent(Character c)` — override per category.

**Shared AI strings — `CharacterAIDebugFormatter`:**
- `string FormatAll(Character c)` — composes all helpers into one debug string. Called by `AISubTab` and `UI_CharacterDebugScript`.
- Individual helpers: `FormatAction`, `FormatBehaviourStack`, `FormatInteraction`, `FormatAgent`, `FormatBusyReason`, `FormatWorkPhaseGoap`, `FormatBt`, `FormatLifeGoap`.

For extension recipes (adding a new `IInspectorView`, adding a new `CharacterSubTab`, module authoring, `IDevAction`), see [.agent/skills/debug-tools/SKILL.md](../../.agent/skills/debug-tools/SKILL.md) and [.agent/skills/dev-mode/SKILL.md](../../.agent/skills/dev-mode/SKILL.md).

## Data flow

**Activation** (release build):
```
Host types "/devmode on" in chat
  → UI_ChatBar routes to DevChatCommands.Handle
  → Host check passes → DevModeManager.Unlock() → TryEnable()
  → Panel GameObject instantiated from Resources/UI/DevModePanel
  → OnDevModeChanged(true) fires → every module shows its UI
```

**Click arbitration** (Spawn vs Select):
```
User arms Spawn toggle
  → DevSpawnModule calls SetClickConsumer(this) → OnClickConsumerChanged fires
  → DevSelectionModule's armed toggle (if on) auto-disarms via its handler
User arms Select toggle
  → symmetric eviction of Spawn
```

**Click-to-spawn** (Spawn module):
```
Armed + ActiveClickConsumer == this + mouse-click on Environment layer
  → DevSpawnModule computes N scatter points (radius = 4 * sqrt(N) units, per rule 32)
  → SpawnManager.SpawnCharacter(...) for each
  → Pre-spawn: Dictionary<int, PendingDevConfig> populated keyed on character.GetInstanceID()
  → Server-only path: SpawnManager.InitializeSpawnedCharacter drains the dict
  → ApplyDevExtras applies combat styles (CharacterCombat.UnlockCombatStyle) and skills
  → CharacterSkills NetworkList replicates skills to all clients natively
```

**Click-to-assign-owner** (Select + DevActionAssignBuilding):
```
Armed Select toggle + click on RigidBody layer
  → DevSelectionModule.SelectedCharacter set → OnSelectionChanged fires
  → DevActionAssignBuilding.IsAvailable(sel) returns true → button enabled
  → Host clicks the action button → Execute claims click slot
  → Next click on Building layer → RaycastHit parent walk → Building subclass
  → CommercialBuilding: SetOwner(character, null)
  → ResidentialBuilding: SetOwner(character)
  → Owner replicates via Room._ownerIds NetworkList<FixedString64Bytes>
```

**Click-to-inspect** (Inspect module):
```
Player clicks on an InteractableObject
  → DevSelectionModule resolves InteractableObject from raycast
  → OnInteractableSelectionChanged(io) fires
  → DevInspectModule.HandleSelection(io)
  → Iterates IInspectorView children via CanInspect(io)
  → First match: SetTarget(io); others: Clear() + SetActive(false)
  → No match: placeholder GO shown
  → CharacterInspectorView: derives Character, activates sub-tab 0 by default
  → Update(): active CharacterSubTab.Refresh(character) every frame
```

**Global shortcuts** (DevModeManager, works on any tab):
```
DevModeManager.Update every frame while IsEnabled
  → HandleGlobalShortcuts()
  → IsTextInputFocused() returns true? → abort
  → ESC pressed? → clear _selectionModule.SelectedInteractable + disarm both toggles
  → Ctrl held + !Alt + !Space + LMB down? → _selectionModule.TrySelectAtCursor(out label)         // interior: RigidBody + Furniture
  → Alt held + !Ctrl + !Space + LMB down? → _selectionModule.TrySelectBuildingAtCursor(out label) // building: Building only
  → Space held + !Ctrl + !Alt + LMB down? → _spawnModule.TrySpawnAtCursor()
  → Ctrl/Alt/Space mutex: any two held = nothing fires (prevents fat-finger spawn + ambiguous picks)
  → Armed click-loops on modules skip their path when Ctrl, Alt, or Space is held (no double-fire)
```

**Authority:** every mutation is host-only (guarded by `IsServer` on the underlying APIs). Clients only observe results through existing networked channels — they never run dev-mode code paths.

## Dependencies

### Upstream (this system needs)
- [[engine-plumbing|spawn-manager]] — `SpawnCharacter(...)` API + `PendingDevConfig` dict + `ApplyDevExtras` post-spawn hook.
- [[engine-plumbing|camera-follow]] — reads `SuppressPlayerInput` to drop the zoom clamp and switch to `LerpUnclamped`.
- [[commercial-building]] — `SetOwner(character, null)` for the "Assign Building" action.
- [[building|ResidentialBuilding]] — `SetOwner(character)` for the same action (polymorphic dispatch from the action).
- [[character]] — the entity being selected and mutated. The interior selection raycast (`RigidBody + Furniture` by default) walks up via `GetComponentInParent<InteractableObject>()` and falls back to `GetComponentInParent<Character>()`. The Inspect tab reads all character subsystems (read-only).
- [[character-combat]] — `UnlockCombatStyle(style, level)` overload used by Spawn module; `CombatSubTab` reads combat state for display.
- [[character-skills]] — its `NetworkList<NetworkSkillSyncData>` handles per-level skill replication for dev-spawned NPCs; `SkillsTraitsSubTab` reads skills for display.
- [[character-needs]] — `NeedsSubTab` reads `CharacterNeeds.AllNeeds` for display.
- [[character-wallet]] — `EconomySubTab` reads `CharacterWallet.GetAllBalances()` for display.
- [[character-work-log]] — `EconomySubTab` reads `CharacterWorkLog.GetAllHistory()` for display.
- [[network]] — host-only gating checks `NetworkManager.Singleton.IsServer`; `Room._ownerIds` NetworkList is where ownership propagates.
- [[player-ui|UI_ChatBar]] — routes `/`-prefixed chat to `DevChatCommands.Handle`.

### Downstream (systems that need this)
- [[character-movement|PlayerController]] — reads `SuppressPlayerInput` to gate right-click move, TAB target, Space attack, combat auto-assignment; reads `GodModeMovementSpeed` for god-mode WASD.
- [[engine-plumbing|PlayerInteractionDetector]] — early-outs on `SuppressPlayerInput` so the E-key path is blocked.
- [[engine-plumbing|camera-follow]] — reads `SuppressPlayerInput` to drop the zoom clamp.

No gameplay system declares a hard dependency on dev mode — it is strictly additive.

## State & persistence

- **Runtime-only.** `IsUnlocked`, `IsEnabled`, `ActiveClickConsumer`, and every module's armed/configured state live in memory on the host. Nothing is serialized to disk or replicated.
- **Clients see nothing.** `DevModeManager` on a client logs "host-only" and no-ops all state transitions.
- **Spawned NPCs persist the same as any other NPC** — once `SpawnManager` finishes, the entity enters the normal [[save-load]] pipeline via `CharacterDataCoordinator`. Dev Mode itself does not own their persistence.
- **Late-joiner replication** of dev-spawn config is incomplete — see gotchas.

## Known gotchas / edge cases

- **Behavioral trait doesn't replicate to late-joining clients.** The server applies the dev-picked trait to the Character, but no dedicated `NetworkVariable` broadcasts it to late-joining / non-host clients. They see defaults until the next full save round-trip. Tracked as Known Limitation §9 in the SKILL. Follow-up slice.
- **Combat style doesn't live-sync.** Only the server applies styles via `CharacterCombat.UnlockCombatStyle(style, level)`. Reconnecting clients rebuild state from save data. Skills, by contrast, already replicate via `CharacterSkills.NetworkList<NetworkSkillSyncData>` and propagate correctly.
- **Personality bug (fixed).** Earlier in development the networked path dropped dev-picked personality because `PendingDevConfig` didn't include it. Fixed in commit `18ae654` by adding a `Personality` field to `PendingDevConfig` and promoting it at the top of `InitializeSpawnedCharacter`.
- **`PendingDevConfig` keying.** Keyed on `character.GetInstanceID()`, NOT `NetworkObject.NetworkObjectId`. Rationale: `NetworkObjectId` is 0 until `Spawn(true)` is called, so a dict keyed on it would miss the drain in the spawn callback. Fixed in commit `2dbbc54`.
- **`BuildingInteractable` masquerades as the Building's interactable.** Since 2026-05-06 the cooperative construction loop added `BuildingInteractable : InteractableObject` on the Building's root GameObject. The Alt+Click raycast resolves an `InteractableObject` *before* the `Building` fallback, so without a special case it would fill `SelectedInteractable` (not `SelectedBuilding`), the `OnBuildingSelectionChanged` event would never fire, and the Inspect tab would never route to `BuildingInspectorView` — leaving the panel blank even though the Select tab label correctly read the building name. `TryRaycastAndSelect` now detects `BuildingInteractable`, walks up to its `Building`, and calls `SetSelectedBuilding` instead. Any future `InteractableObject` subclass added directly on a Building's root GameObject must extend the same special case (or Alt+Click will silently break again).
- **Runtime sub-tab hosts collapsed under the prefab's outer `VerticalLayoutGroup`.** When `BuildingInspectorView.Awake` programmatically appended `SubTabBar` / `OverviewContent` / `ConsoleManagementContent` to the prefab GO, two compounding traps left the active content host clipped to ~30 px tall: (1) the prefab's pre-existing `Slots` wrapper kept its `LayoutElement(min=200, flex=10)` and hogged most of the parent VLG's vertical space; (2) even after hiding `Slots`, the parent VLG's flex distribution misbehaved with mixed `flex=0` / `flex=1` siblings + a TMP-derived `Header` preferred height. Fix lands in commit `ef136b57` and is documented in [[runtime-uichildren-collapse-under-prefab-vlg]]. The pattern: hide every legacy prefab child that isn't a directly-referenced serialized field, then build runtime hosts with `LayoutElement.ignoreLayout = true` + manual stretch anchors instead of fighting VLG flex distribution. See `BuildingInspectorView.ConfigureContentHostStretch` for the canonical helper.
- **Click target layers for selection (dual mask).** `DevSelectionModule` raycasts against two named masks resolved at `Start` when the serialized fields are left at zero:
  - `_selectableLayerMask` (Ctrl+Click — interior) defaults to `RigidBody + Furniture + Harvestable`. Building is intentionally absent so a building shell collider doesn't block selection of the chest or NPC inside. `Harvestable` is included so any `Harvestable` (crop or wilderness) sitting on layer index 15 — named `Harvestable` in the Tags & Layers list — is pickable. Prefabs authored on a different layer (e.g. `Tree.prefab` / `Gatherable.prefab` on `Default`) will not be selectable until they're moved onto a layer in the mask.
  - `_buildingLayerMask` (Alt+Click — building) defaults to `Building`.
  Renaming or deleting any of those layers in **Tags & Layers** silently degrades selection (the resolver tolerates missing names — `BuildMask` skips them and only logs an error when **both** masks resolve to zero). Override either field on the `DevModePanel` prefab to narrow the mask if a project ever needs to exclude one of those layers.
- **Click target layer for building ownership.** `DevActionAssignBuilding` raycasts against `LayerMask.GetMask("Building")` directly. Same fragility — changing the Building layer breaks this action silently. Fail loud by logging a warning if the raycast misses.
- **No ScrollView in the panel.** Long combat-style / skill lists overflow vertically. Deferred polish.
- **No exclude-self filter.** Clicking on the host's own character selects it. Intentional for now (you may want to assign yourself as building owner); add a toggle if it becomes annoying.
- **Nested NetworkObject warning at client-join** (intermittent). Pre-existing issue unrelated to dev mode — some building prefabs (`Small house`, `Forge`) host two NetworkObjects. Dev Mode's `DevActionAssignBuilding` does not create the nesting; it just calls `SetOwner`. Tracked under [[building]] refactor work.
- **Camera zoom snaps on dev-mode exit.** When dev mode turns off, `_targetZoom` re-clamps to `[0, 1]` and the camera smoothly returns to the normal range. No visible jitter under normal use, but extreme zoom-outs take a moment to spring back.

## Open questions / TODO

- [ ] Replicate behavioral trait to late-joining / non-host clients (follow-up slice).
- [ ] Add live `NetworkList<>`-based combat style sync to match the skills path.
- [ ] Add ScrollView to the panel so long combat-style / skill lists don't overflow.
- [ ] Add a visual selection indicator (shader-based outline per rule 25, or UI marker) on the selected Character.
- [ ] Add "click building first, then assign a character to it" reverse flow.
- [ ] Multi-character selection.
- [ ] Follow-up modules: Freecam, Sim-pause, Item grant, Teleport, Time-of-day slider, Assign Job.
- [x] ~~Wire the Inspect tab prefab (Task 17) — `DevInspectModule`, `CharacterInspectorView`, 10 sub-tab GOs.~~ Shipped 2026-04-23 via `Assets/Editor/DevMode/DevInspectTabBuilder.cs`.
- [ ] Refine `KnowledgeSubTab` (BookKnowledge portion) and `InventorySubTab` once their public APIs are stabilized. Schedule portion of Knowledge is fully rendered as of 2026-04-23.
- [ ] Add `IInspectorView` implementation for `WorldItem`. (`Building` is shipped via `IBuildingInspectorView`/`BuildingInspectorView` since 2026-04 and now hosts a 2-sub-tab structure as of 2026-05-09.)
- [ ] Optionally split `BuildingOverviewSubTab` into per-section sub-tabs (Identity / State / Owners / Commercial / Inventory / Logistics / Tasks / Rooms / Furniture / Interior). Currently a single TMP-text dump — fine for read-only inspection; only worth splitting if dev workflows demand it.
- [ ] Move wilderness `Harvestable` prefabs (`Tree.prefab`, `Gatherable.prefab`) onto the `Harvestable` layer (currently they sit on `Default`) so Ctrl+Click selects them too. Currently only `CropHarvestable` is selectable because it's the only harvestable family on the `Harvestable` layer.

## Change log
- 2026-05-14 — `BuildingOverviewSubTab` Furniture section now prints `role=<X>` after the type-name suffix for any `StorageFurniture`. Reflects role changes from every trigger — player UI mutator, NPC shift-punch auto-assignment (`BuildingLogisticsManager.AssignStorageRolesForShift`), dev-mode `DevForceSetStorageRole`, save-restore. Polls every frame via `BuildingInspectorView.Update`, so the next replication tick is picked up automatically. Playtest-confirmed. See [[commercial-storage-roles]]. — claude
- 2026-04-21 — Initial page. Documents F3/chat activation, input gating, click arbitration, Spawn + Select modules, `IDevAction` contract, `DevActionAssignBuilding`, god-mode WASD speed + unbounded zoom. — Claude / [[kevin]]
- 2026-04-23 — Added Inspect tab (IInspectorView + CharacterInspectorView + 10 sub-tabs); generalized DevSelectionModule to InteractableObject; extracted CharacterAIDebugFormatter; updated Key classes, Public API, Data flow, Dependencies, Open questions, Sources. — claude
- 2026-04-23 — Inspect tab prefab wired via DevInspectTabBuilder Editor utility. Global shortcuts (Ctrl+Click, Space+LMB, ESC) relocated to DevModeManager so they work on any tab. Social / SkillsTraits / Economy / Knowledge sub-tab rendering polished: relationship details with HasMet flag, behavioural profile name + personality description + compatibility lists, per-CurrencyId and per-JobType enumeration + flat Workplaces list sorted by score, full Schedule rendering with active-now highlight. — claude
- 2026-04-25 — Added `StorageFurnitureInspectorView` so storage furniture (chests, shelves, barrels, wardrobes — anything inheriting `StorageFurniture`) can be selected via Ctrl+Click and inspected in the Inspect tab (capacity / locked / full + per-slot listing). View is added to the prefab by the new `DevStorageFurnitureInspectorBuilder` Editor utility (`Tools/DevMode/Build Storage Furniture Inspector`). No changes to selection or `DevInspectModule` — the view is auto-discovered. — claude
- 2026-04-25 — Split selection raycast into two masks. `DevSelectionModule._selectableLayerMask` (Ctrl+Click "interior") auto-defaults to `RigidBody + Furniture` — Building is intentionally excluded so a building's shell collider doesn't block selection of the chest, bed, or NPC inside it. New `_buildingLayerMask` field auto-defaults to `Building` and is consumed by the new **Alt + Left-Click** global shortcut (`TrySelectBuildingAtCursor`). Mutex extended: Ctrl / Alt / Space are mutually exclusive on the same click. Authored prefab masks still override the runtime defaults; the armed click loop now also short-circuits on Alt. — claude
- 2026-04-29 — Added `HarvestableInspectorView` so any `Harvestable` (wilderness or farmed crop) can be Ctrl+Click-selected and inspected in the Inspect tab. Identity / Harvestable state / Destruction sections are common; a Crop section pulls live `CropHarvestableNetSync` state and the full `TerrainCell` readout when the target is a `CropHarvestable`. View is added to the prefab by the new `DevHarvestableInspectorBuilder` Editor utility (`Tools/DevMode/Build Harvestable Inspector`); auto-discovered at runtime by `DevInspectModule`. `DevSelectionModule._defaultInteriorLayers` extended from `RigidBody + Furniture` to `RigidBody + Furniture + Harvestable` so any prefab on the `Harvestable` layer (index 15) is pickable. Layer 15 was renamed `Crop → Harvestable` in `ProjectSettings/TagManager.asset` to reflect that crop harvestables and wilderness harvestables share the layer. Wilderness harvestables (`Tree.prefab`, `Gatherable.prefab`) currently sit on `Default` and are tracked as a follow-up. — claude
- 2026-05-08 — Fixed Alt+Click building selection regression introduced by the 2026-05-06 cooperative construction loop. `BuildingInteractable : InteractableObject` lives on the Building's root GameObject (`[RequireComponent(typeof(Building))]`), so the raycast resolved it as an `InteractableObject` and called `SetSelectedInteractable` instead of `SetSelectedBuilding`. `OnBuildingSelectionChanged` never fired, the Inspect tab found no `IInspectorView` matching `BuildingInteractable`, and showed the empty placeholder — even though the Select tab label correctly read the building name. `DevSelectionModule.TryRaycastAndSelect` now special-cases `BuildingInteractable`: walks up to its `Building` and routes through `SetSelectedBuilding`. The plain `Building`-fallback branch is preserved for buildings without the construction interactable. Stale class header comment claiming "Buildings have no InteractableObject in the parent chain" updated to reflect the new reality. — claude
- 2026-04-26 — Fixed Spawn-tab layout regression introduced when the Character/Item sub-tabs were added (commits 78e9a8d + a6d8396). Two prefab-side root causes: (1) the top `TabBar` and Spawn `SubTabBar` had no `LayoutElement`, so the parent VLG queried their `LayoutGroup` for a flexible height it couldn't compute and one of them ate most of the panel height. (2) `CharacterSubPanel` and `ItemSubPanel` were authored with center-stretch anchors `(0,0) → (1,1)` and `SizeDelta(0,0)`, which makes them report a rect height equal to the parent — when `SpawnTab.VLG.ChildControlHeight` was 0, the VLG positioned siblings using that bogus height and `Label_Count` collided with `Label_Item`. Fix: added `LayoutElement` (PreferredHeight 36 / 32, FlexibleHeight 0) on `TabBar` / `SubTabBar`; switched the two sub-panels to top-stretch anchors `(0,1) → (1,1)` with pivot `(0.5,1)`, gave them `LayoutElement` (FlexibleHeight 1) so the active one fills the remaining vertical space; flipped `SpawnTab.VLG.ChildControlHeight` to 1 so the VLG honours those preferred heights. Renamed the Item-tab label from "Item Override:" to "Item:" — the sub-tab itself selects mode, the dropdown is no longer an "override". The new layout contract is documented in [.agent/skills/dev-mode/SKILL.md](../../.agent/skills/dev-mode/SKILL.md) §7. — claude
- 2026-05-09 — Added Console Management sub-tab on `BuildingInspectorView` (mirror of `CharacterInspectorView` — new `BuildingSubTab` base class + `BuildingOverviewSubTab` wraps the prior 11-section read-out + `BuildingConsoleManagementSubTab` is the new mutator surface). Tab bar built programmatically at runtime (no prefab edits, no Editor builder needed for 2 sub-tabs). Console Management exposes every owner-gated mutation on `CommercialBuilding` / `ShopBuilding`: hiring open/close, owner add/remove/transfer/clear, force-hire / force-fire per Job, per-storage role assignment via `StorageRoleType` (None / Tool / Inventory / SellShelf), catalog add/remove, cashier withdraw → host, building-inventory add/remove by `ItemId`. Owner-gated paths bypass the auth check via new `DevForce*` server methods on `CommercialBuilding` (`DevForceSetHiring`, `DevForceSetAssignmentWage`, `DevForceSetStorageRole`) and `ShopBuilding` (`DevForceAdd/Remove/EditCatalogEntry`, `DevForceWithdrawCashierTill`). Each `DevForce*` is wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD` and gated by `DevAssertHostAndDevMode` (asserts `IsServer` + `DevModeManager.IsEnabled`, emits an audit log line). No new RPCs and no `devOverride` boolean: dev mode is host-only by hard policy (`DevModeManager.TryEnable` rejects non-host callers), so the dev panel runs on the server and calls server methods directly. Production owner-gated `ServerRpc` / `[Rpc(SendTo.Server)]` entry points are unchanged — `Do*` helpers were extracted from each ShopBuilding catalog / cashier RPC body so production and dev paths share the same effect, with the auth check living only on the production caller. — claude
- 2026-05-09 — Fixed runtime sub-tab hosts collapsing to ~30 px under the prefab's outer `VerticalLayoutGroup` (commit `ef136b57`). Root cause was twofold: the prefab's legacy `Slots` wrapper still hogged space via `LayoutElement(min=200, flex=10)`, and the parent VLG's flex distribution misbehaved with mixed `flex=0` / `flex=1` siblings. `BuildingInspectorView.BuildSubTabHierarchy` now (1) hides every legacy prefab child that isn't a serialized field reference, and (2) bypasses the parent VLG entirely for the runtime hosts — `LayoutElement.ignoreLayout = true` + manual stretch anchors via the new `ConfigureContentHostStretch` helper. Documented as [[runtime-uichildren-collapse-under-prefab-vlg]]. — claude
- 2026-05-13 — Added Game Speed module (`DevGameSpeedModule`). New dev tab exposing preset speed buttons (0× / 1× / 2× / 4× / 8×) plus a free-form `TMP_InputField` for arbitrary values (slow-mo 0.25×, 16× stress-test, etc.). All routes through `GameSpeedController.RequestSpeedChange` — server-authoritative, fully replicated to clients via the existing `_serverTimeScale` `NetworkVariable`. Subscribes to `OnSpeedChanged` in OnEnable for live visual refresh; falls back to writing `Time.timeScale` directly when no networked controller exists (solo / pre-spawn). Prefab wired in `Assets/Resources/UI/DevModePanel.prefab` via MCP script-execute — new `SpeedTabButton` under `ContentRoot/TabBar` (cloned from `TimeSkipTabButton`), new `SpeedTab` content under `ContentRoot` (cloned from `TimeSkipTab`, child UI rebuilt with 5 preset buttons + custom input + apply + status label), `DevGameSpeedModule` attached with all `[SerializeField]` refs assigned, new entry appended to `DevModePanel._tabs` so the existing `SwitchTab` loop picks it up. Replaces the "Sim-pause" planned-module entry. Mirrors `UI_GameSpeedController` but adds the custom value field for dev iteration outside the player HUD's fixed 5-button vocabulary. — claude

## Sources
- [DevModeManager.cs](../../Assets/Scripts/Debug/DevMode/DevModeManager.cs) — singleton, state, events.
- [DevModePanel.cs](../../Assets/Scripts/Debug/DevMode/DevModePanel.cs) — panel root + tab registry.
- [DevChatCommands.cs](../../Assets/Scripts/Debug/DevMode/DevChatCommands.cs) — `/devmode` parser.
- [DevSpawnModule.cs](../../Assets/Scripts/Debug/DevMode/Modules/DevSpawnModule.cs) — click-to-spawn.
- [DevSelectionModule.cs](../../Assets/Scripts/Debug/DevMode/Modules/DevSelectionModule.cs) — click-to-select; generalized to `InteractableObject`.
- [IDevAction.cs](../../Assets/Scripts/Debug/DevMode/Modules/Actions/IDevAction.cs) — plug-in contract.
- [DevActionAssignBuilding.cs](../../Assets/Scripts/Debug/DevMode/Modules/Actions/DevActionAssignBuilding.cs) — first shipping action.
- [IInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/IInspectorView.cs) — Inspect dispatch contract.
- [DevInspectModule.cs](../../Assets/Scripts/Debug/DevMode/Inspect/DevInspectModule.cs) — Inspect tab dispatcher.
- [CharacterInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/CharacterInspectorView.cs) — 10-tab character inspector view.
- [CharacterAIDebugFormatter.cs](../../Assets/Scripts/Debug/DevMode/Inspect/CharacterAIDebugFormatter.cs) — shared AI debug string helpers.
- [CharacterSubTab.cs](../../Assets/Scripts/Debug/DevMode/Inspect/SubTabs/CharacterSubTab.cs) — abstract sub-tab base.
- `Assets/Scripts/Debug/DevMode/Inspect/SubTabs/` — IdentitySubTab, StatsSubTab, SkillsTraitsSubTab, NeedsSubTab, AISubTab, CombatSubTab, SocialSubTab, EconomySubTab, KnowledgeSubTab, InventorySubTab.
- [StorageFurnitureInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/StorageFurnitureInspectorView.cs) — `IInspectorView` for `StorageFurniture` (chests, shelves, barrels, wardrobes).
- [DevStorageFurnitureInspectorBuilder.cs](../../Assets/Editor/DevMode/DevStorageFurnitureInspectorBuilder.cs) — Editor-only one-shot prefab builder for the storage view.
- [HarvestableInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/HarvestableInspectorView.cs) — `IInspectorView` for `Harvestable` (wilderness harvestables + crop harvestables); pulls extra crop-specific state from `CropHarvestableNetSync` + `TerrainCellGrid` when applicable.
- [DevHarvestableInspectorBuilder.cs](../../Assets/Editor/DevMode/DevHarvestableInspectorBuilder.cs) — Editor-only one-shot prefab builder for the harvestable view.
- [Harvestable.cs](../../Assets/Scripts/Interactable/Harvestable.cs) — `InteractableObject`-derived base for harvestables; the inspected target.
- [CropHarvestable.cs](../../Assets/Scripts/Farming/CropHarvestable.cs) — `Harvestable` subclass for farmed crops; surfaces `CellX` / `CellZ` / `Grid`.
- [CropHarvestableNetSync.cs](../../Assets/Scripts/Farming/CropHarvestableNetSync.cs) — sibling NetworkBehaviour hosting `CurrentStage` / `IsDepleted` / `CropIdNet` NetVars consumed by the inspector.
- [CropSO.cs](../../Assets/Scripts/Farming/Pure/CropSO.cs) — crop content definition; resolved from `CropIdNet` via `CropRegistry`.
- [StorageFurniture.cs](../../Assets/Scripts/World/Furniture/StorageFurniture.cs) — slot-based container; the inspected target.
- [FurnitureInteractable.cs](../../Assets/Scripts/Interactable/FurnitureInteractable.cs) — `InteractableObject` wrapper that exposes the underlying `Furniture` (used by `CanInspect`).
- [UI_CharacterDebugScript.cs](../../Assets/Scripts/UI/WorldUI/UI_CharacterDebugScript.cs) — legacy overlay; now delegates AI strings to `CharacterAIDebugFormatter`.
- [SpawnManager.cs](../../Assets/Scripts/SpawnManager.cs) — extended spawn API consumed by Spawn module.
- [PlayerController.cs](../../Assets/Scripts/Character/CharacterControllers/PlayerController.cs) — input gate (WASD kept live at god-mode speed; action inputs suppressed).
- [PlayerInteractionDetector.cs](../../Assets/Scripts/Character/PlayerInteractionDetector.cs) — E-key gate.
- [CameraFollow.cs](../../Assets/Scripts/CameraFollow.cs) — unbounded zoom when dev mode is active.
- [CommercialBuilding.cs](../../Assets/Scripts/World/Buildings/CommercialBuilding.cs) — `SetOwner` target for first action; `_ownerIds` NetworkList replication.
- [.agent/skills/debug-tools/SKILL.md](../../.agent/skills/debug-tools/SKILL.md) — procedural source of truth: Inspect tab, IInspectorView, CharacterSubTab, CharacterAIDebugFormatter, and legacy script patterns.
- [.agent/skills/dev-mode/SKILL.md](../../.agent/skills/dev-mode/SKILL.md) — procedural how-to (module authoring, action authoring, Spawn + Select UI wiring).
- [.claude/agents/debug-tools-architect.md](../../.claude/agents/debug-tools-architect.md) — specialist agent for dev-mode + other debug infra.
- 2026-04-21 conversation with [[kevin]] — god-mode movement speed (17), unbounded zoom, `RigidBody` layer for character picks, commercial-building owner replication fix.
- 2026-04-23 implementation — Inspect tab (16 commits, 76e470e–8a1a90f): IInspectorView, DevInspectModule, CharacterInspectorView, 10 CharacterSubTab subclasses, CharacterAIDebugFormatter, DevSelectionModule generalization.
- 2026-04-23 follow-up — Prefab wiring + polish pass (commits caa8275, 7559138, 0deea49, e8231e2, fe2baae, 6fa15f1, e005608, 2c455a6, e36d404): DevInspectTabBuilder Editor script, relationship details, behavioural profile + personality, Ctrl+Click / Space+LMB / ESC shortcuts consolidated on DevModeManager, per-currency / per-JobType enumeration, flat Workplaces list, full Schedule rendering.
