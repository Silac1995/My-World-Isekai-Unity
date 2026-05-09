# WorldItem Physics & Pathing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `WorldItem`s full physics citizens — no more `FreezeOnGround` — and give them a `NavMeshObstacle` enabled on first ground contact so AI agents path around them, while characters can still bump them aside to avoid the drop-at-feet stuck state.

**Architecture:** Items keep their existing non-kinematic Rigidbody (so explosions/projectiles can fling them) on the existing `RigidBody` physics layer (so character-vs-item collisions still happen, enabling self-unstick). A `NavMeshObstacle` (carve=true) is added to the prefab, **disabled by default**, and enabled by the server in `OnCollisionEnter` once the item touches anything. Activation propagates to all clients via a server-write `NetworkVariable<bool>` so each peer's local navmesh carves correctly. The `FreezeOnGround` mechanism is deleted — root cause of the original stuck bug.

**Tech Stack:** Unity 6 (URP), Unity NGO (Netcode for GameObjects), NavMesh + NavMeshObstacle (`UnityEngine.AI`), C#.

**Spec:** [docs/superpowers/specs/2026-04-22-worlditem-physics-and-pathing-design.md](../specs/2026-04-22-worlditem-physics-and-pathing-design.md)

---

## File Inventory

| File | Operation | Responsibility |
|---|---|---|
| `Assets/Resources/Data/Item/ItemSO.cs` | Modify | Add `BlocksPathing` SerializeField + property. |
| `Assets/Prefabs/Items/WorldItem_prefab.prefab` | Modify | Add `NavMeshObstacle` (disabled, carve=true). Re-tune Rigidbody. Wire `_navMeshObstacle` ref on WorldItem component. |
| `Assets/Scripts/Item/WorldItem.cs` | Modify | Add `_navMeshObstacle` field, `_obstacleActive` NetworkVariable, augment `OnNetworkSpawn`/`OnNetworkDespawn`, rewrite `OnCollisionEnter`, delete `FreezeOnGround`. |
| `Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs` | Modify | Drop `_freezeOnGround` field, constructor parameter, and `ExecutePhysicalDrop` parameter. |
| `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` | Modify | Drop `freeze` parameter from `RequestItemDropServerRpc` signature and body. |
| `Assets/Scripts/Inventory/UI_ItemSlot.cs` | Modify | Update `new CharacterDropItem(...)` call site (drop 3rd arg). |
| `Assets/Scripts/AI/Actions/BTAction_PunchOut.cs` | Modify | Update call site. |
| `Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs` | Modify | Update call site. |
| `Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs` | Modify | Update two call sites (lines 149, 178). |
| `Assets/Scripts/AI/GOAP/Actions/GoapAction_DeliverItem.cs` | Modify | Update call site. |
| `.agent/skills/item_system/SKILL.md` | Modify | Document new physics/pathing model and `BlocksPathing`. |
| `.agent/skills/navmesh-agent/SKILL.md` | Modify | Note WorldItem now contributes carved holes; document tuning. |
| `wiki/systems/world-items.md` | Modify | Bump `updated:`, change log entry, public API delta, gotchas note. |
| `.claude/agents/item-inventory-specialist.md` | Modify | Add new behaviors to the agent's domain summary. |

---

## Notes for the implementer

- **No automated test framework in this project.** Verification is via Unity Editor (compile / domain reload) + manual play-test scenarios. Each task includes the exact play-test repro.
- **Unity is connected via MCP.** Prefab edits use `mcp__ai-game-developer__*` tools (`assets-prefab-open`, `gameobject-component-add`, `gameobject-component-modify`, `assets-prefab-save`, `assets-prefab-close`). Falling back to direct YAML editing of `.prefab` files is brittle (GUID handling) — prefer MCP.
- **After every code change**, wait for the Unity domain reload to complete and then check `mcp__ai-game-developer__console-get-logs` for compile errors before committing.
- **Multiplayer testing**: requires running two Editor instances or one Editor + a built client. Host=server. If you only have one machine, ParrelSync or a Standalone build alongside the Editor is the standard approach. If a multiplayer test rig is unavailable, mark the multiplayer test step as `[~]` (deferred) and flag it to the user.

---

## Task 1: Add `BlocksPathing` field to `ItemSO`

**Files:**
- Modify: `Assets/Resources/Data/Item/ItemSO.cs`

**Why first:** Additive, no other code depends on the new field yet. Compiles immediately. Sets up the data layer used by Task 4.

- [ ] **Step 1: Add the field and property**

In `Assets/Resources/Data/Item/ItemSO.cs`, after the `Rendering` block (after the `CastsShadow` property, around line 40), add a new `Pathing` block:

```csharp
    [Header("Pathing")]
    [Tooltip("If true, this item gets a NavMeshObstacle when it lands so AI agents path around it. " +
             "Set to false for trivial items (trash, coins, single grain) that shouldn't litter the navmesh.")]
    [SerializeField] private bool _blocksPathing = true;

    public bool BlocksPathing => _blocksPathing;
```

- [ ] **Step 2: Verify Unity compiles**

Trigger a domain reload (use `mcp__ai-game-developer__assets-refresh` or just save the file). Then call `mcp__ai-game-developer__console-get-logs` and confirm zero errors.

Expected: clean compile.

- [ ] **Step 3: Commit**

```bash
git add Assets/Resources/Data/Item/ItemSO.cs
git commit -m "feat(items): add ItemSO.BlocksPathing flag for NavMeshObstacle opt-out"
```

---

## Task 2: Modify `WorldItem` prefab — add NavMeshObstacle and re-tune Rigidbody

**Files:**
- Modify: `Assets/Prefabs/Items/WorldItem_prefab.prefab`

**Why second:** Prefab change is independent of code (the `_navMeshObstacle` field on `WorldItem.cs` doesn't exist yet, so there's nothing to wire). We add the component now and wire it in Task 3.

- [ ] **Step 1: Open the prefab via MCP**

```
mcp__ai-game-developer__assets-prefab-open
  prefabAssetPath: "Assets/Prefabs/Items/WorldItem_prefab.prefab"
```

- [ ] **Step 2: Find the root GameObject and inspect current state**

```
mcp__ai-game-developer__gameobject-find
  name: "WorldItem_prefab"
```

Note the GameObject's instance ID for the next steps.

- [ ] **Step 3: Add the NavMeshObstacle component**

```
mcp__ai-game-developer__gameobject-component-add
  gameObjectRef: { name: "WorldItem_prefab" }
  componentTypeName: "UnityEngine.AI.NavMeshObstacle"
```

- [ ] **Step 4: Configure NavMeshObstacle fields**

```
mcp__ai-game-developer__gameobject-component-modify
  gameObjectRef: { name: "WorldItem_prefab" }
  component: {
    type: "UnityEngine.AI.NavMeshObstacle",
    members: {
      m_Shape: 1,                  // Box
      m_Center: { x: 0, y: 0.5, z: 0 },
      m_Size: { x: 1, y: 1, z: 1 },
      m_Carve: true,
      m_MoveThreshold: 0.1,
      m_TimeToStationary: 0.5,
      m_CarveOnlyStationary: true,
      m_Enabled: false             // Disabled at spawn — enabled at runtime by OnCollisionEnter
    }
  }
```

If the MCP `members` syntax differs in this project, fall back to: open the prefab in the Unity Editor (Project window → double-click `WorldItem_prefab`), Add Component → Nav Mesh Obstacle, set Shape=Box, Center=(0, 0.5, 0), Size=(1,1,1), check Carve, Move Threshold=0.1, Time To Stationary=0.5, check Carve Only Stationary, **uncheck the Enabled checkbox at the top of the component**.

- [ ] **Step 5: Re-tune the Rigidbody**

```
mcp__ai-game-developer__gameobject-component-modify
  gameObjectRef: { name: "WorldItem_prefab" }
  component: {
    type: "UnityEngine.Rigidbody",
    members: {
      m_Mass: 2,
      m_LinearDamping: 3,
      m_AngularDamping: 4
    }
  }
```

(Leave `m_IsKinematic`, `m_CollisionDetection`, `m_Interpolate`, `m_UseGravity` at their current values: 0, 0, 0, 1.)

- [ ] **Step 6: Save and close the prefab**

```
mcp__ai-game-developer__assets-prefab-save
mcp__ai-game-developer__assets-prefab-close
```

- [ ] **Step 7: Sanity-check the YAML**

Read `Assets/Prefabs/Items/WorldItem_prefab.prefab` and confirm:
- A new `NavMeshObstacle` block exists with `m_Carve: 1`, `m_Enabled: 0`.
- The Rigidbody block now shows `m_Mass: 2`, `m_LinearDamping: 3`, `m_AngularDamping: 4`.
- The `WorldItem_prefab` root `m_Component` list now includes the new NavMeshObstacle's fileID.

- [ ] **Step 8: Commit**

```bash
git add Assets/Prefabs/Items/WorldItem_prefab.prefab
git commit -m "feat(items): WorldItem prefab adds disabled NavMeshObstacle, retunes Rigidbody"
```

---

## Task 3: Add `_navMeshObstacle` field + `_obstacleActive` NetworkVariable + handlers in `WorldItem.cs`

**Files:**
- Modify: `Assets/Scripts/Item/WorldItem.cs`

**Why third:** Additive — augments `OnNetworkSpawn` / `OnNetworkDespawn` and adds a no-op `OnObstacleActiveChanged`. The new NetworkVariable starts at `false` and is never set true yet, so behavior is unchanged. Sets up the wiring used in Task 4.

- [ ] **Step 1: Add `using UnityEngine.AI`**

At the top of `Assets/Scripts/Item/WorldItem.cs` (after the existing using-statements):

```csharp
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Unity.Collections;
```

- [ ] **Step 2: Add the `_navMeshObstacle` field next to existing references**

Find the `[Header("References")]` block (line 8). Update it to:

```csharp
    [Header("References")]
    [SerializeField] private Transform _visualRoot; // Glisse l'objet "Visual" de ton prefab ici
    [SerializeField] private NavMeshObstacle _navMeshObstacle;
```

- [ ] **Step 3: Auto-find the obstacle in `Awake()`**

Find the existing `Awake()` (line 33). Replace the body with:

```csharp
    private void Awake()
    {
        SortingGroup = GetComponent<UnityEngine.Rendering.SortingGroup>();
        if (_navMeshObstacle == null) _navMeshObstacle = GetComponent<NavMeshObstacle>();
    }
```

- [ ] **Step 4: Add the `_obstacleActive` NetworkVariable**

Right below the existing `_networkItemData` declaration (around line 21–26), add:

```csharp
    [SerializeField]
    private NetworkVariable<bool> _obstacleActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
```

- [ ] **Step 5: Add the `OnObstacleActiveChanged` handler**

Add a new private method anywhere in the class (suggest after `OnItemDataChanged`, around line 60):

```csharp
    private void OnObstacleActiveChanged(bool previousValue, bool newValue)
    {
        if (_navMeshObstacle != null) _navMeshObstacle.enabled = newValue;
    }
```

- [ ] **Step 6: Augment `OnNetworkSpawn` and `OnNetworkDespawn`**

Replace the existing `OnNetworkSpawn` (line 38) with:

```csharp
    public override void OnNetworkSpawn()
    {
        _networkItemData.OnValueChanged += OnItemDataChanged;
        _obstacleActive.OnValueChanged += OnObstacleActiveChanged;

        // Late-joiner: apply current obstacle state immediately
        if (_obstacleActive.Value && _navMeshObstacle != null)
            _navMeshObstacle.enabled = true;

        // Apply data if joining late as a client
        if (IsClient && !IsServer)
        {
            ApplyNetworkData(_networkItemData.Value);
        }
    }
```

Replace the existing `OnNetworkDespawn` (line 49) with:

```csharp
    public override void OnNetworkDespawn()
    {
        _networkItemData.OnValueChanged -= OnItemDataChanged;
        _obstacleActive.OnValueChanged -= OnObstacleActiveChanged;
    }
```

- [ ] **Step 7: Verify Unity compiles**

`mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs`. Expected: clean compile, zero warnings about missing references.

- [ ] **Step 8: Wire the prefab's NavMeshObstacle reference**

The new `_navMeshObstacle` `[SerializeField]` is empty in the prefab. Open the prefab again and assign:

```
mcp__ai-game-developer__assets-prefab-open
  prefabAssetPath: "Assets/Prefabs/Items/WorldItem_prefab.prefab"

mcp__ai-game-developer__gameobject-component-modify
  gameObjectRef: { name: "WorldItem_prefab" }
  component: {
    type: "WorldItem",
    members: {
      _navMeshObstacle: { ref: "WorldItem_prefab/NavMeshObstacle" }
    }
  }

mcp__ai-game-developer__assets-prefab-save
mcp__ai-game-developer__assets-prefab-close
```

If the MCP path syntax for component refs differs, fall back to the Editor: select the WorldItem_prefab root in Project view, drag the NavMeshObstacle from the Inspector's component list into the new "Nav Mesh Obstacle" slot on the WorldItem component.

The Awake-time fallback (`GetComponent<NavMeshObstacle>()`) means even an unwired prefab works at runtime, but explicit wiring is more robust and clearer in the Inspector.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts/Item/WorldItem.cs Assets/Prefabs/Items/WorldItem_prefab.prefab
git commit -m "feat(items): wire NavMeshObstacle field + _obstacleActive NetworkVariable on WorldItem"
```

---

## Task 4: Rewrite `WorldItem.OnCollisionEnter` — drop FreezeOnGround branch, activate NavMeshObstacle

**Files:**
- Modify: `Assets/Scripts/Item/WorldItem.cs`

**Why fourth:** This is the behavior change. Drops no longer freeze. Server-side `OnCollisionEnter` now flips `_obstacleActive` to true on first contact, which propagates to all peers via the NetworkVariable wired in Task 3.

The `FreezeOnGround` *field* still exists after this task (callers in CharacterActions.cs and elsewhere still write to it), but its read site is gone. Task 5 cleans up the field and all writers.

- [ ] **Step 1: Replace `OnCollisionEnter`**

Find the existing `OnCollisionEnter` (line 328). Replace the entire method with:

```csharp
    private void OnCollisionEnter(Collision collision)
    {
        // Server-authoritative: only the server decides when an item locks into the navmesh.
        if (!IsServer) return;
        if (_obstacleActive.Value) return;
        if (_itemInstance == null || _itemInstance.ItemSO == null) return;
        if (!_itemInstance.ItemSO.BlocksPathing) return;

        // Setting the NetworkVariable propagates to every peer; OnObstacleActiveChanged
        // enables their local NavMeshObstacle.
        _obstacleActive.Value = true;
    }
```

- [ ] **Step 2: Verify Unity compiles**

`mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs`. Expected: clean compile. The `FreezeOnGround` property still exists, so other callers continue to compile.

- [ ] **Step 3: Manual play-test — drop & no longer freeze**

In the Unity Editor:
1. Open the GameScene.
2. Press Play. Spawn a player character (whatever the project's standard spawn flow is — probably automatic).
3. Pick up an item from the world (or have one in inventory).
4. Drop it (right-click → drop in `UI_ItemSlot` if that's the bound action; otherwise the existing drop UI).
5. **Observe**: the item falls, lands. It is NOT kinematic (push it physically with a rigidbody force, or just walk into it — it should slide).
6. **Observe Console**: an old log line `WorldItem ... ground freeze engaged.` should NOT appear (it was in the old `FreezeOnGround` branch).
7. **Observe**: walk past the dropped item with another character (or with the player on NavMeshAgent mode if applicable) — the agent should now path around it (the NavMeshObstacle carved a hole on collision).

If the agent doesn't path around: open the Scene view, enable the AI navigation overlay (Window → AI → Navigation, or NavMesh visualization gizmos), confirm a hole exists where the item lies. If no hole: check that the prefab's NavMeshObstacle was correctly enabled at runtime (select the dropped WorldItem in Hierarchy, see if NavMeshObstacle is checked).

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Item/WorldItem.cs
git commit -m "feat(items): WorldItem drops are no longer kinematic; NavMeshObstacle activates on first contact"
```

---

## Task 5: Remove `FreezeOnGround` from WorldItem and all 8 callers (atomic)

**Files (all in one commit):**
- Modify: `Assets/Scripts/Item/WorldItem.cs` — remove the `FreezeOnGround` field
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs` — remove `_freezeOnGround` field, ctor param, `ExecutePhysicalDrop` param, and the `spawnedItem.FreezeOnGround = freeze` write + the `freeze` arg passed to the RPC
- Modify: `Assets/Scripts/Character/CharacterActions/CharacterActions.cs` — remove `freeze` param from `RequestItemDropServerRpc` signature and body
- Modify: `Assets/Scripts/Inventory/UI_ItemSlot.cs` line 64 — drop the third arg (`false`)
- Modify: `Assets/Scripts/AI/Actions/BTAction_PunchOut.cs` line 94 — drop the third arg (`true`)
- Modify: `Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs` line 225 — drop the third arg (`true`)
- Modify: `Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs` lines 149 + 178 — drop the third arg (`true`) on both
- Modify: `Assets/Scripts/AI/GOAP/Actions/GoapAction_DeliverItem.cs` line 55 — drop the third arg (`true`)

**Why atomic:** removing `FreezeOnGround` from `WorldItem` while a single caller still writes to it = compile failure. All 8 files must change in the same commit.

- [ ] **Step 1: Remove the `FreezeOnGround` field from `WorldItem.cs`**

In `Assets/Scripts/Item/WorldItem.cs`, delete line 19:

```csharp
    public bool FreezeOnGround { get; set; } = false;
```

- [ ] **Step 2: Update `CharacterDropItem.cs`**

In `Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs`, replace the entire file with:

```csharp
using UnityEngine;
using UnityEngine.TextCore.Text;

public class CharacterDropItem : CharacterAction
{
    private ItemInstance _itemInstance;

    public CharacterDropItem(Character character, ItemInstance item) : base(character, 0.5f)
    {
        _itemInstance = item ?? throw new System.ArgumentNullException(nameof(item));
    }

    public override void OnStart()
    {
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null) animator.SetTrigger("Trigger_Drop");

        Debug.Log($"{character.CharacterName} prepare le drop.");
    }

    public override void OnApplyEffect()
    {
        bool removed = false;

        var equip = character.CharacterEquipment;
        if (equip != null && equip.HaveInventory())
        {
            if (equip.GetInventory().RemoveItem(_itemInstance, character))
            {
                removed = true;
            }
        }

        if (!removed)
        {
            var hands = character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.CarriedItem == _itemInstance)
            {
                hands.DropCarriedItem();
                removed = true;
            }
        }

        if (removed)
        {
            ExecutePhysicalDrop(character, _itemInstance);
        }
    }

    /// <summary>
    /// Helper statique pour forcer un drop physique immédiat sans passer par l'Animator.
    /// Utile lors de la mort, de l'incapacitation ou de l'entrée en combat.
    /// </summary>
    public static void ExecutePhysicalDrop(Character owner, ItemInstance item)
    {
        if (owner == null || item == null) return;

        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            // Server: spawn directly
            Vector3 dropPos = owner.transform.position + Vector3.up * 1.5f;
            Vector3 offset = new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            WorldItem.SpawnWorldItem(item, dropPos + offset);

            Debug.Log($"<color=cyan>[CharacterDropItem]</color> {item.ItemSO.ItemName} dropped in world (server).");
        }
        else if (owner.CharacterActions != null)
        {
            // Client: request server to spawn the dropped item
            string jsonData = JsonUtility.ToJson(item);
            owner.CharacterActions.RequestItemDropServerRpc(
                item.ItemSO.ItemId,
                jsonData,
                owner.transform.position
            );
            Debug.Log($"<color=cyan>[CharacterDropItem]</color> {item.ItemSO.ItemName} drop requested from client.");
        }
    }
}
```

- [ ] **Step 3: Update `CharacterActions.RequestItemDropServerRpc`**

In `Assets/Scripts/Character/CharacterActions/CharacterActions.cs`, replace the existing RPC (line 161) with:

```csharp
    [Rpc(SendTo.Server)]
    public void RequestItemDropServerRpc(string itemId, string jsonData, Vector3 ownerPosition)
    {
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO so = System.Array.Find(allItems, match => match.ItemId == itemId);
        if (so == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find ItemSO '{itemId}' for drop.");
            return;
        }

        ItemInstance instance = so.CreateInstance();
        JsonUtility.FromJsonOverwrite(jsonData, instance);
        instance.ItemSO = so;

        Vector3 dropPos = ownerPosition + Vector3.up * 1.5f;
        Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0, UnityEngine.Random.Range(-0.3f, 0.3f));
        WorldItem.SpawnWorldItem(instance, dropPos + offset);

        Debug.Log($"<color=green>[CharacterActions]</color> Server spawned dropped item: {so.ItemName}");
    }
```

(The `WorldItem spawnedItem = ...` assignment + `spawnedItem.FreezeOnGround = freeze` lines are gone.)

- [ ] **Step 4: Update `UI_ItemSlot.cs:64`**

In `Assets/Scripts/Inventory/UI_ItemSlot.cs`, replace line 64:

```csharp
                var dropAction = new CharacterDropItem(_uiInventory.CharacterOwner, _itemSlot.ItemInstance, false);
```

with:

```csharp
                var dropAction = new CharacterDropItem(_uiInventory.CharacterOwner, _itemSlot.ItemInstance);
```

- [ ] **Step 5: Update `BTAction_PunchOut.cs:94`**

In `Assets/Scripts/AI/Actions/BTAction_PunchOut.cs`, replace line 94:

```csharp
                var dropAction = new CharacterDropItem(self, carriedItem, true);
```

with:

```csharp
                var dropAction = new CharacterDropItem(self, carriedItem);
```

- [ ] **Step 6: Update `GoapAction_GatherStorageItems.cs:225`**

In `Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs`, replace line 225:

```csharp
                    var dropAction = new CharacterDropItem(worker, carriedItem, true);
```

with:

```csharp
                    var dropAction = new CharacterDropItem(worker, carriedItem);
```

- [ ] **Step 7: Update `GoapAction_DepositResources.cs:149` and `:178`**

In `Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs`, replace line 149:

```csharp
                var dropAction = new CharacterDropItem(worker, carriedItem, true);
```

with:

```csharp
                var dropAction = new CharacterDropItem(worker, carriedItem);
```

And line 178:

```csharp
                    var dropAction = new CharacterDropItem(worker, item, true);
```

with:

```csharp
                    var dropAction = new CharacterDropItem(worker, item);
```

- [ ] **Step 8: Update `GoapAction_DeliverItem.cs:55`**

In `Assets/Scripts/AI/GOAP/Actions/GoapAction_DeliverItem.cs`, replace line 55:

```csharp
            return new CharacterDropItem(worker, currentItem, true);
```

with:

```csharp
            return new CharacterDropItem(worker, currentItem);
```

- [ ] **Step 9: Verify Unity compiles**

`mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__console-get-logs`. Expected: clean compile, no errors about `FreezeOnGround` not existing or `CharacterDropItem` constructor mismatch.

If any caller you missed errors out: search for `FreezeOnGround` and `new CharacterDropItem(` across the project (`grep -r "FreezeOnGround" Assets/ ; grep -r "new CharacterDropItem(" Assets/`) and update.

- [ ] **Step 10: Commit**

```bash
git add Assets/Scripts/Item/WorldItem.cs \
        Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs \
        Assets/Scripts/Character/CharacterActions/CharacterActions.cs \
        Assets/Scripts/Inventory/UI_ItemSlot.cs \
        Assets/Scripts/AI/Actions/BTAction_PunchOut.cs \
        Assets/Scripts/AI/GOAP/Actions/GoapAction_GatherStorageItems.cs \
        Assets/Scripts/AI/GOAP/Actions/GoapAction_DepositResources.cs \
        Assets/Scripts/AI/GOAP/Actions/GoapAction_DeliverItem.cs
git commit -m "refactor(items): remove FreezeOnGround mechanism — root cause of drop-at-feet stuck bug"
```

---

## Task 6: Manual gameplay verification

**Files:** none — Editor play-test only.

This task runs the spec's section 7 test plan against the implemented code. **Do not commit anything in this task** unless a step fails and requires a code fix.

For each scenario, log the result. If any scenario fails, return to the relevant Task (4 for runtime behavior, 5 for callers) and fix.

- [ ] **Test A — Drop & walk away**
  1. Pick up an item, walk to an open area, drop it.
  2. Walk in a circle around it. Item stays put.
  3. Confirm via Scene view (with NavMesh gizmos enabled): a small carved hole appears under the item.
  4. **Pass criterion:** Item is settled, agent paths around it, no console errors.

- [ ] **Test B — Drop & walk through (stuck recovery)**
  1. Have an NPC (any GOAP-driven NPC) walking on a fixed path.
  2. Drop an item directly in their path 1u in front of them.
  3. **Pass criterion:** NPC nudges the item aside or walks around it within 2 seconds. Does NOT freeze in place. The existing stuck-detection sliding code should not have to kick in (no `[Movement] ... Chemin instable` warning in console).

- [ ] **Test C — Drop in narrow doorway**
  1. Find a building doorway (any commercial building entrance).
  2. Drop an item directly in the doorway.
  3. Send another character through the doorway (e.g., another player or an NPC routed there).
  4. **Pass criterion:** Character either pushes through the item or detours, but doesn't deadlock.

- [ ] **Test D — Stack drop**
  1. Pick up 5 of the same item.
  2. Drop them one by one in roughly the same spot.
  3. They form a small pile (some bouncing/settling is expected).
  4. **Pass criterion:** All 5 items rest on the ground (or on each other) within 2 seconds. NPCs path around the pile.

- [ ] **Test E — Trash item (BlocksPathing=false)**
  1. Pick any existing low-importance item SO (e.g., a coin or trash item; if none exists, create a temporary test SO).
  2. In the Inspector, uncheck the `Blocks Pathing` toggle.
  3. Drop one in front of an NPC.
  4. **Pass criterion:** NPC walks straight through the item (no carved hole). Item still gets bumped physically (a slight nudge as the NPC passes).
  5. **Restore the SO's `Blocks Pathing` to true after the test** (revert with `git checkout` on the SO asset if changed).

- [ ] **Test F — Pickup**
  1. Drop an item, wait for the carved hole to appear.
  2. Pick it up.
  3. **Pass criterion:** Carved hole disappears, NavMesh restores. Other dropped items in the area are unaffected.

- [ ] **Test G — Multiplayer (Host + Client)**

  Requires two Editor instances or Editor + standalone build.

  1. Host starts the scene, joins as player A. Client connects as player B.
  2. **Host drops an item.** Confirm:
     - Both players see the item land.
     - Both players' NavMeshAgents path around it (have an NPC walk through the area on each peer).
  3. **Client drops an item.** Confirm same behavior.
  4. **Pass criterion:** Both peers see correct obstacle behavior. Open Console on both — no replication errors, no `_obstacleActive` race warnings.

  If multiplayer test rig is unavailable, skip and flag the user.

- [ ] **Test H — Late joiner**

  1. Host starts. Drop 3 items.
  2. **Then** have a Client connect.
  3. **Pass criterion:** The newly-connected Client sees obstacles in the correct positions immediately on spawn (the `OnNetworkSpawn` late-joiner branch in `WorldItem.cs` applies the current `_obstacleActive` value).

- [ ] **Test I — Explosion / impulse (manual rigidbody force)**

  Since impact-damage is out of scope, simulate an explosion via the Console:
  1. Drop an item.
  2. With the item selected, open the Inspector → Rigidbody → click the kebab → **(or)** use a quick test script to call `rb.AddExplosionForce(500, transform.position - Vector3.up, 5);`.
  3. **Pass criterion:** Item flies, tumbles, lands somewhere new. Within ~1 second of stopping (carving's `Time To Stationary`), the new position is carved on the navmesh and the old position's hole is gone.

- [ ] **Result logging**

  Document pass/fail for each test in the PR description (or in this plan as `[x]` for pass, `[!]` for fail). If any test fails, return to the relevant Task and fix before moving on.

---

## Task 7: Documentation updates (project rules 28, 29, 29b)

**Files:**
- Modify: `.agent/skills/item_system/SKILL.md`
- Modify: `.agent/skills/navmesh-agent/SKILL.md`
- Modify: `wiki/systems/world-items.md`
- Modify: `.claude/agents/item-inventory-specialist.md`

**Why last:** Documentation describes the as-built behavior. Easier to write accurately once the code is final and play-tested.

- [ ] **Step 1: Read each file before editing**

```bash
# Read each to understand the existing structure
cat .agent/skills/item_system/SKILL.md
cat .agent/skills/navmesh-agent/SKILL.md
cat wiki/systems/world-items.md
cat .claude/agents/item-inventory-specialist.md
```

- [ ] **Step 2: Update `.agent/skills/item_system/SKILL.md`**

Add (or update) a section titled "WorldItem physics & pathing model". Content to include:

```markdown
## WorldItem Physics & Pathing

**Physics state**
- WorldItems are non-kinematic Rigidbodies on the **RigidBody** layer (layer 8).
- Layer matrix: RigidBody ↔ Default (characters) is **enabled** — characters can physically push items aside (this is what prevents drop-at-feet stuck cases).
- Default mass = 2, linear damping = 3, angular damping = 4 (tuned for the project's 11 units = 1.67m scale).
- Items are gravity-affected, sleep automatically when settled.
- The `FreezeOnGround` mechanism has been removed (was the root cause of drop-at-feet stuck bugs).

**AI pathing**
- Each WorldItem prefab carries a `NavMeshObstacle` (carve=true), **disabled at spawn**.
- The server-side `OnCollisionEnter` enables it on first contact (with anything — ground, another item, a wall).
- Activation propagates to all peers via `_obstacleActive` (`NetworkVariable<bool>`, server-write, everyone-read). Each peer's local `OnObstacleActiveChanged` enables their own `NavMeshObstacle` so each navmesh carves correctly. Late-joiners apply the current value in `OnNetworkSpawn`.
- Items can opt out via `ItemSO.BlocksPathing = false` (use for trash, coins).

**Tuning (NavMeshObstacle)**
- `Move Threshold = 0.1` — small character-bumps don't trigger re-carve.
- `Time To Stationary = 0.5s` — tumbling items wait until still before re-carving.
- `Carve Only Stationary = true` — moving items contribute nothing to the navmesh.

**Performance posture**
- Settled items cost ~0 (Rigidbody Sleep + carving's stationary handling).
- Drop event = one carve-create. Pickup event = one carve-destroy. No per-frame cost.
- If carving cost ever becomes a problem (hundreds of items in a hot area), the lever is distance-based NavMeshObstacle hibernation with hysteresis. Not implemented today.

**Carried items**
- `HandsController.AttachVisualToHand` instantiates the WorldItem prefab as a hand visual but immediately sets `Rigidbody.isKinematic = true` and disables all colliders. The carried clone never collides with anything, so its `NavMeshObstacle` never activates.
```

- [ ] **Step 3: Update `.agent/skills/navmesh-agent/SKILL.md`**

Add a short section at an appropriate place:

```markdown
## NavMeshObstacle sources

- **Buildings**: `Building.cs` triggers a full `NavMeshSurface.BuildNavMesh()` rebake on spawn so building geometry is precisely included.
- **WorldItems**: each carries a `NavMeshObstacle` (carve=true) enabled by the server on first ground contact, propagated to clients via `_obstacleActive` `NetworkVariable<bool>`. Items opt out via `ItemSO.BlocksPathing = false`. No global rebake — runtime carving only. See `.agent/skills/item_system/SKILL.md` for tuning details.

If your agent is failing to path around an item: confirm the item's `ItemSO.BlocksPathing` is true and that `_obstacleActive` is true on the peer that owns the agent.
```

- [ ] **Step 4: Update `wiki/systems/world-items.md`**

Bump the `updated:` frontmatter date to today's date (`2026-04-22`).

Add a `## Change log` entry (or append to existing):

```markdown
- 2026-04-22 — Removed FreezeOnGround. Items are now permanent non-kinematic physics objects. Added NavMeshObstacle (carve=true) enabled on first ground contact, replicated via `_obstacleActive` NetworkVariable. New `ItemSO.BlocksPathing` flag (default true) for opt-out. Re-tuned base prefab Rigidbody (mass 30→2, drag 0→3, angular drag 0.05→4). — claude
```

Update the Public API section to remove `FreezeOnGround` (gone) and add:
- `_obstacleActive : NetworkVariable<bool>` — server-set on first collision, replicates to clients.
- `OnObstacleActiveChanged(bool, bool)` — handler that enables the local NavMeshObstacle.

Add a `## Gotchas` entry (or append):

```markdown
- The "RigidBody" physics layer (layer 8) ↔ Default layer (0) collision must remain **enabled** in the project's Physics matrix. Disabling it would break the drop-at-feet stuck recovery (characters could no longer push items aside).
- NavMeshObstacle carving is event-driven, not per-frame. Stationary items cost ~0; spawning/despawning many items at once does pay a per-event carve cost. If item churn becomes a hotspot (hundreds of items in a small area), the deferred fix is distance-based obstacle hibernation.
```

Refresh `depends_on` / `depended_on_by` if they don't already include `ai-pathing` / `ai-navmesh`.

Add a link in `## Sources` to the new spec:

```markdown
- [docs/superpowers/specs/2026-04-22-worlditem-physics-and-pathing-design.md](../../docs/superpowers/specs/2026-04-22-worlditem-physics-and-pathing-design.md)
```

- [ ] **Step 5: Update `.claude/agents/item-inventory-specialist.md`**

In the agent's domain summary, add a bullet covering the new behaviors. Suggested addition (place near existing WorldItem coverage):

```markdown
- **WorldItem physics/pathing**: items are non-kinematic Rigidbody on layer "RigidBody"; `NavMeshObstacle` (carve=true) is enabled by the server on first ground contact and replicated via `_obstacleActive` NetworkVariable. `ItemSO.BlocksPathing` opts trash items out. The `FreezeOnGround` field is removed — never reintroduce it (it was the root cause of drop-at-feet character-stuck bugs).
```

- [ ] **Step 6: Commit**

```bash
git add .agent/skills/item_system/SKILL.md \
        .agent/skills/navmesh-agent/SKILL.md \
        wiki/systems/world-items.md \
        .claude/agents/item-inventory-specialist.md
git commit -m "docs: WorldItem physics & pathing — update SKILL, wiki, agent"
```

---

## Task 8: Final verification & handoff

- [ ] **Step 1: Confirm all 7 tasks committed**

```bash
git log --oneline -10
```

Expected commits (newest first):
1. docs: WorldItem physics & pathing — update SKILL, wiki, agent
2. refactor(items): remove FreezeOnGround mechanism — root cause of drop-at-feet stuck bug
3. feat(items): WorldItem drops are no longer kinematic; NavMeshObstacle activates on first contact
4. feat(items): wire NavMeshObstacle field + _obstacleActive NetworkVariable on WorldItem
5. feat(items): WorldItem prefab adds disabled NavMeshObstacle, retunes Rigidbody
6. feat(items): add ItemSO.BlocksPathing flag for NavMeshObstacle opt-out
7. (spec commits from brainstorming phase, already in place)

- [ ] **Step 2: Run network-validator agent (per project rule 19)**

Dispatch the `network-validator` agent on the modified files to confirm Host↔Client, Client↔Client, Host/Client↔NPC parity:

```
Files to validate:
- Assets/Scripts/Item/WorldItem.cs
- Assets/Scripts/Character/CharacterActions/CharacterDropItem.cs
- Assets/Scripts/Character/CharacterActions/CharacterActions.cs
```

Address any flags from the validator. Re-commit fixes as `fix(items): address network-validator findings`.

- [ ] **Step 3: Final test sweep**

Re-run Tests A, B, F from Task 6 to confirm nothing regressed during the doc/cleanup phase.

- [ ] **Step 4: Hand back to user**

Report:
- All 7 functional tasks committed.
- Manual test results from Task 6 (pass/fail/skipped per scenario).
- Network-validator findings (clean / specific issues).
- Files changed summary.
- Any deferred items (e.g., multiplayer test if rig unavailable).

---

## Self-Review (run after writing the plan)

**1. Spec coverage:**
- Section 4.1 (Physics layer) → no code change, covered in Task 7 docs (gotcha).
- Section 4.2 (Rigidbody tuning) → Task 2 step 5.
- Section 4.3 (NavMeshObstacle on prefab) → Task 2 steps 3–4.
- Section 4.4 (sketch) → Task 4 (uses 4.7's networked version per spec note).
- Section 4.5 (`BlocksPathing`) → Task 1.
- Section 4.6 (FreezeOnGround removal) → Task 5.
- Section 4.7 (NetworkVariable model) → Tasks 3+4 combined.
- Section 5 (Files to change) → File Inventory at top + per-task Files lists.
- Section 6 (Documentation) → Task 7.
- Section 7 (Test plan) → Task 6 (one test per spec scenario, plus explosion test).
- Section 8 (Out of scope) → not implemented (correct).
- Section 9 / 9b (Open / resolved questions) → no implementation action needed.

All spec sections accounted for.

**2. Placeholder scan:** No "TBD", "TODO", "implement later", or "similar to Task N" present. Every code step shows full code. Every command shows the exact invocation.

**3. Type consistency:**
- `_navMeshObstacle` (field name) consistent across Task 2 step 4, Task 3 steps 2/3/8, Task 7 step 2.
- `_obstacleActive` consistent across Task 3 steps 4/5/6, Task 4 step 1, Task 7.
- `BlocksPathing` (property), `_blocksPathing` (field) consistent: Task 1, Task 4, Task 7.
- `OnObstacleActiveChanged(bool, bool)` signature consistent across Task 3 step 5 and Task 7 docs.
- `RequestItemDropServerRpc(string, string, Vector3)` (no freeze param) consistent: Task 5 steps 2 + 3.
- `new CharacterDropItem(Character, ItemInstance)` (2-arg ctor) consistent: Task 5 steps 2, 4, 5, 6, 7, 8.
