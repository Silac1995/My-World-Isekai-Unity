# Quest System — Manual Smoke Test

**Branch:** `multiplayyer`
**Date:** 2026-04-23
**Companion to:** `docs/superpowers/specs/2026-04-23-quest-system-design.md`

Manual Play Mode tests. The architectural pieces have no automated coverage in v1 (the wage spec's pure-logic asmdef pattern doesn't apply — Quest code mostly references project types). This file is the verification path before you call the system shipped.

---

## Prerequisites

- `GameScene` open. `WageSystemService` GameObject still in the scene root (from the wage system).
- The Quest HUD prefabs exist and are wired on `PlayerUI`:
  - `UI_QuestTracker` prefab + GameObject under the player HUD canvas.
  - `UI_QuestLogWindow` prefab + GameObject (initially inactive).
  - `QuestWorldMarkerRenderer` GameObject with the 3 marker prefabs (`QuestMarker_Diamond`, `QuestMarker_Beacon`, `QuestZone_Fill`) wired.
- A `Character_Default` variant prefab spawnable via dev-mode. Confirm `CharacterQuestLog` child exists with component + `_character` backref wired (Task 20).
- A placed `CommercialBuilding` (Sawmill, Smithy, Shop, etc.) with at least one open job slot.

If your dev-mode Inspect tab can show `CharacterQuestLog.ActiveQuests` and `_snapshots`, use it. Otherwise temporarily expose them via a debug button or read in the Inspector when paused.

---

## Test 1 — Player Harvester full shift (auto-claim, zone fill, completion)

**Setup:**
1. Enter Play Mode.
2. Spawn an NPC and assign yourself as a Woodcutter at a HarvestingBuilding (Sawmill) using dev-mode `Hire`.
3. Walk to the building so the player is on-shift via the existing punch-in path.

**Run:**
1. Watch the HUD. Expected: `UI_QuestTracker` appears with the title "Harvest Resource" and instruction line like "Harvest 5 Tree (0 / 5)".
2. The HarvestableZone gets a gold ground-fill overlay; individual harvestable trees get floating diamonds above them.
3. Walk to a tree and chop it (existing harvest flow). Tracker progress increments.
4. After chopping enough, quest completes — drops out of the tracker; if another harvest task auto-publishes, it takes over.

**Expected:**
- `Character.CharacterQuestLog.ActiveQuests` count went from 0 → 1 → 0 (or → 1 again if a follow-up auto-publishes).
- `OnQuestAdded` fires once on claim, `OnQuestProgressChanged` fires on each chop, `OnQuestRemoved` fires on completion.
- Wage system continues to work — `CharacterWallet` balance increments on punch-out.

**Failure modes to watch:**
- HUD never appears → check `PlayerUI._questTrackerUI` is wired in GameScene.
- No quest auto-claim → check `Character_Default` prefab actually has the `CharacterQuestLog` child component (Task 20). Check console for `[CharacterQuestLog]` warnings.
- Zone fill doesn't render → check `QuestWorldMarkerRenderer._zoneFillPrefab` is wired and the `Zone.Bounds` of the HarvestableZone is non-empty.

---

## Test 2 — Player Transporter (BuildingTarget beacon)

**Setup:** Hire as Transporter at a building that has placed a TransportOrder.

**Run:** Walk on-shift; quest auto-claims; a vertical light shaft beacon appears at the destination's `DeliveryZone`.

**Expected:**
- Tracker shows "Transport Goods — Deliver N Item to DestinationName".
- Beacon at destination, no zone fill, no diamond.
- After delivery, completion flows.

---

## Test 3 — Player Crafter shared (multi-contributor)

**Setup:** Two players (or 1 player + 1 NPC) hired as Blacksmiths at the same Smithy with an active CraftingOrder for Quantity > 1.

**Run:** Both punch in. Both auto-claim the SAME CraftingOrder (MaxContributors = int.MaxValue). Each crafts items.

**Expected:**
- Both players see the same Quest in their tracker.
- Progress is SHARED — each item crafted by either player increments TotalProgress.
- Per-character contribution dict tracks each player's contribution separately (visible in `_log.FocusedQuest.Contribution`).
- Quest log window's "Contributors" section lists both players with per-player counts.

---

## Test 4 — Abandon

**Setup:** Player has at least one active quest.

**Run:**
1. Open quest log (L key).
2. Select a quest.
3. Click "Abandon".

**Expected:**
- Quest disappears from `ActiveQuests`. `OnQuestRemoved` fires.
- If the building still has the quest open and the player is still on-shift, **the quest will auto-re-claim immediately** (no cooldown by design — clean re-claim per spec). To prevent this in the test, either punch out first or quit the job.
- If another active quest exists, focus auto-shifts to it.

---

## Test 5 — Save / Load round-trip

**Setup:** Complete Test 1 setup so player has 1+ active quests + a focused one.

**Run:**
1. Save via the bed checkpoint or portal gate.
2. Quit Play Mode and re-enter.
3. Load the save.

**Expected:**
- `CharacterQuestLog.ActiveQuests` reconciles from saved snapshots — quests still in the building's `GetAvailableQuests()` re-attach (player rejoins as contributor); quests no longer resolvable drop with a warning.
- `FocusedQuest` restored.
- HUD re-renders.

**Failure modes:**
- Quest snapshots saved but resolution fails → check `BuildingManager.Instance.allBuildings` was populated when Deserialize ran. The reconciliation runs at Character spawn; if buildings load AFTER characters, the reconciliation needs a deferred retry (not in v1; flag for follow-up).

---

## Test 6 — Map transition + dormant snapshots

**Setup:** Active harvest quest on Map A.

**Run:**
1. Travel to Map B via portal gate. Map A hibernates.
2. Open quest log on Map B.
3. Travel back to Map A.

**Expected:**
- On Map B: Quest appears in log window as "Pending — return to Map A" (badge shown for snapshots whose `originMapId != currentMapId`). Markers do NOT render in Map B.
- On return to Map A: `HandleMapChanged` promotes the dormant snapshot — quest reactivates, markers re-render.

**Note:** v1 disallows abandoning Pending quests via UI (Abandon button greyed out for dormant snapshots). The plan punted "abandon-on-dormant" to a future polish.

---

## Test 7 — Multiplayer (Host ↔ Client wallet sync analogue)

**Setup:** Host + Client both connected. Both spawn Characters at the same Sawmill, both hired as Woodcutters.

**Run:** Both punch in.

**Expected:**
- Both clients' HUDs show the same shared Harvest quest (since MaxContributors = 10, both can join).
- Server publishes the quest, ClientRpc snapshot pushes hit each owning client.
- Each client sees their own per-character contribution number in tracker, plus the shared total progress.

**v1 limitation:** late-joining client (joins AFTER another client claimed a quest) sees their own log populated via NetworkList initial sync, but the snapshot ClientRpc only fires on join → state-changed events going forward. Pre-existing snapshots delivered via the join sync. This is the same model as the wage's late-joiner gap.

---

## Test 8 — Latent bug catch: BuildingTask QuestId stability

**Setup:** Spawn a single HarvestableTree, manually trigger a `BuildingTaskManager.RegisterTask(new HarvestResourceTask(tree))`. Note the QuestId.

**Run:** Re-instantiate via DevMode (or scene reload). Note the new QuestId.

**Expected:**
- QuestIds are unique per-instance (each `new BuildingTask(...)` gets a fresh `Guid.NewGuid().ToString("N")`).
- This means quests are not stable across server restarts. v1 acceptance: Quests live in the same server lifetime; save/load re-publishes fresh quests on building load and the character's saved snapshot resolves to the new instance only if the task content matches by id (it won't — design limitation).
- **Real implication:** Saving mid-quest, then quitting + reloading the world, will cause player snapshots to enter dormant state (id no longer found) and eventually drop. Per spec section 9 edge cases, this is documented behavior.

If this becomes a real issue (player notices quest lost across save/load), promote QuestId from auto-Guid to a stable hash of (BuildingId + TaskTypeName + TargetId).

---

## Known v1 Limitations (do NOT report as bugs)

1. **Late-joining clients miss prior snapshots until next mutation.** Same as wage system. Upgrade path: NetworkList<QuestSnapshot> when Kingdom currencies arrive.
2. **`CharacterQuestLog.ResolveQuest` is O(buildings × quests).** Linear scan over `BuildingManager.allBuildings`. Future `QuestRegistry` singleton.
3. **`OriginWorldId` is empty string** — no source for it yet (would need `WorldAssociation` singleton accessible to buildings). Map-id filtering still works.
4. **Abandon-on-dormant disabled** — UI greys the Abandon button for dormant snapshots.
5. **QuestId regenerates per-instance** — not stable across server restarts (see Test 8). Save/load may drop quests if the underlying task instance is recreated.
6. **No completed-quest history** — completed quests vanish from the log entirely.
7. **HarvestResourceTask.Required is dynamic** — drops as the resource depletes. If a player joins after others have chopped, their `Required` value will be smaller than the original.

---

## Pass / Fail Criteria

This smoke test passes if:
- All 8 scenarios behave as expected (with v1 limitations explicitly accepted).
- No new exceptions in the Unity console during any scenario.
- Wage system continues to work (sanity check — wage tests + a quick punch-out wage payment).

Report results in the next conversation turn.
