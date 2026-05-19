# Speech Bubble Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-speaker coloured name strip + downward tail to every speech bubble, drive the name with a new `Relationship.KnowsName` flag (falling back to `???`), and replace the binary distance proximity gate with a linear fade matching the action-indicator HUD. Refactor the dead separator-line pipeline out.

**Architecture:** Each speech bubble gains two new visual children (NameStrip + Tail) inside its existing root prefab; per-speaker accent colour comes from `CharacterArchetype.AccentColor` (with a per-character override on `Character` replicated via `NetworkVariable<Color32>`). The display name resolution lives on `SpeechBubbleStack` and consults the local player's `CharacterRelation.GetRelationshipWith(speaker)?.KnowsName` — falling back to `"???"`. Stacking logic + RPC routing + dialogue API are untouched.

**Tech Stack:** Unity 2D-in-3D, NGO (Netcode for GameObjects), TextMeshPro, UGUI Canvas. EditMode tests via NUnit (`Assets/Tests/EditMode/`).

**Source spec:** [docs/superpowers/specs/2026-05-19-speech-bubble-rework-design.md](../specs/2026-05-19-speech-bubble-rework-design.md)

---

## File Structure

**Modified (10 files):**

| Path | Change |
|---|---|
| `Assets/Scripts/Character/CharacterRelation/Relationship.cs` | Add `_knowsName` field + `KnowsName` getter + `SetKnowsName(bool)` setter. |
| `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs` | Extend `CharacterRelationSyncData` with `KnowsName`; thread through sync/save round-trip. |
| `Assets/Scripts/Character/Archetype/CharacterArchetype.cs` | Add `_accentColor` SerializeField + `AccentColor` getter. |
| `Assets/Scripts/Character/Character.cs` | Add `AccentColor` property + `NetworkVariable<Color32>` + override resolver + `SetAccentColor(Color)` setter. |
| `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs` | Add `AccentColorOverride` + `HasAccentColorOverride` fields. |
| `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs` | Add `_nameStripBackground` / `_nameText` / `_tailRoot` fields + `SetSpeakerDisplay` + `SetIsNewest`; drop `_separatorLine` + `SetSeparatorVisible`. |
| `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs` | Add `ResolveSpeakerDisplay`, `IsLocalPlayerStack`, tail-newest tracking, linear fade math. Drop `_proximityRadius`, `UpdateSeparatorVisibility`. |
| `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` | Restructure: NameStrip + Body + Tail children. Drop SeparatorLine. |
| `Assets/Resources/Data/CharacterArchetypes/*.asset` (each existing archetype) | Author `_accentColor` for designer defaults. |
| Various wiki + skill files | Doc pass per rule #28 + #29b. |

**Created (1 test file + 1 asmdef):**

| Path | Purpose |
|---|---|
| `Assets/Tests/EditMode/CharacterRelation/RelationshipKnowsNameTests.cs` | EditMode test for `KnowsName` field default + setter + serialize round-trip math. |
| `Assets/Tests/EditMode/CharacterRelation/CharacterRelation.Tests.asmdef` | Asmdef wrapping the new test file. References Assembly-CSharp via `MWI` (or matching pattern from other test asmdefs that test classes in the default assembly — see existing test scaffold). |

---

## Task 1 — Add `KnowsName` to `Relationship`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterRelation/Relationship.cs`

- [ ] **Step 1: Add the field, getter, and setters next to the existing `_hasMet` shape**

In `Assets/Scripts/Character/CharacterRelation/Relationship.cs`, locate the existing `_hasMet` declaration (around line 10) and the `SetAsMet/SetAsNotMet/ToggleMetStatus` methods (around line 68-70). Add the parallel `_knowsName` field + accessors right next to them.

After line 10 (`[SerializeField] private bool _hasMet = false;`), insert:

```csharp
[SerializeField] private bool _knowsName = false;
```

After line 28 (`public bool HasMet => _hasMet;`), insert:

```csharp
public bool KnowsName => _knowsName;
```

After line 70 (`public void ToggleMetStatus() => _hasMet = !_hasMet;`), insert:

```csharp
public void SetKnowsName(bool value)
{
    if (_knowsName == value) return;
    _knowsName = value;
    Debug.Log($"<color=cyan>[Relation KnowsName]</color> {_character.CharacterName} -> {_relatedCharacter.CharacterName} : KnowsName = {value}");
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Character/CharacterRelation/Relationship.cs
git commit -m "feat(relation): add KnowsName flag separate from HasMet"
```

---

## Task 2 — Extend `CharacterRelationSyncData` and sync paths

**Files:**
- Modify: `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs`

- [ ] **Step 1: Add `KnowsName` to the sync data struct**

Locate the `CharacterRelationSyncData` struct (top of file, around line 8-29). Add a `KnowsName` field, extend `NetworkSerialize` and `Equals`.

Add after `public bool HasMet;` (line 12):

```csharp
public bool KnowsName;
```

In `NetworkSerialize` (after `serializer.SerializeValue(ref HasMet);` on line 19):

```csharp
serializer.SerializeValue(ref KnowsName);
```

In the `Equals` chain (around line 27, after the `HasMet == other.HasMet` line) — but watch the trailing semicolon. The chain becomes:

```csharp
return targetCharacterId == other.targetCharacterId && 
       RelationValue == other.RelationValue && 
       RelationType == other.RelationType && 
       HasMet == other.HasMet &&
       KnowsName == other.KnowsName;
```

- [ ] **Step 2: Thread `KnowsName` through the sync data → Relationship import paths**

Find each spot that reads `syncData.HasMet` and applies it (`SetAsMet` / `SetAsNotMet`) — they live around lines 154, 193, 252. After each `SetAsMet/SetAsNotMet` call, add the parallel `SetKnowsName` call.

Pattern (replace the block at each callsite):

```csharp
if (syncData.HasMet) existing.SetAsMet();
else existing.SetAsNotMet();
existing.SetKnowsName(syncData.KnowsName);
```

For the constructor branch (line 152-154 area):

```csharp
existing = new Relationship(Character, target, syncData.RelationValue, syncData.RelationType);
if (syncData.HasMet) existing.SetAsMet();
existing.SetKnowsName(syncData.KnowsName);
```

- [ ] **Step 3: Thread `KnowsName` into the sync data export**

Find the `BuildSyncData` (or equivalent) helper around line 207-212 — wherever the struct is constructed from a `Relationship`:

```csharp
return new CharacterRelationSyncData
{
    targetCharacterId = rel.RelatedCharacter.CharacterId,
    RelationValue = rel.RelationValue,
    RelationType = rel.RelationType,
    HasMet = rel.HasMet,
    KnowsName = rel.KnowsName
};
```

Also extend the NetworkList diff check around line 218 — add `_networkRelations[i].KnowsName != syncData.KnowsName` to the OR chain that triggers replacement.

- [ ] **Step 4: Compile + run Editor briefly**

In Unity Editor: `Ctrl+R` (Assets → Refresh) to trigger compile. Watch the Console — no errors expected. If you renamed a sync field or broke a callsite, the compile error tells you exactly where.

Expected: clean compile.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs
git commit -m "feat(relation): thread KnowsName through sync data + import paths"
```

---

## Task 3 — Save round-trip for `KnowsName`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs`

- [ ] **Step 1: Add `knowsName` to `RelationshipSaveEntry`**

Find the `RelationshipSaveEntry` struct (likely in the same file, near the bottom — search for `[System.Serializable]` near `RelationSaveData`). Add a new field:

```csharp
public bool knowsName;
```

- [ ] **Step 2: Extend Serialize**

In `Serialize()` (line 354), the entry block builds `hasMet = rel.HasMet`. Add the parallel line:

```csharp
var entry = new RelationshipSaveEntry
{
    targetCharacterId = rel.RelatedCharacter.CharacterId,
    targetWorldGuid = rel.RelatedCharacter.OriginWorldGuid ?? "",
    relationshipType = (int)rel.RelationType,
    relationValue = rel.RelationValue,
    hasMet = rel.HasMet,
    knowsName = rel.KnowsName
};
```

- [ ] **Step 3: Extend Deserialize**

In `Deserialize(...)` (line 388), find every callsite of `SetAsMet`/`SetAsNotMet` and add the parallel `SetKnowsName(entry.knowsName)` call.

Constructor branch (line 405-407):

```csharp
var rel = new Relationship(_character, target, entry.relationValue, (RelationshipType)entry.relationshipType);
if (entry.hasMet) rel.SetAsMet();
rel.SetKnowsName(entry.knowsName);
_relationships.Add(rel);
```

Existing-relationship branch (line 411-414):

```csharp
existing.RelationValue = entry.relationValue;
existing.SetRelationshipType((RelationshipType)entry.relationshipType);
if (entry.hasMet) existing.SetAsMet();
else existing.SetAsNotMet();
existing.SetKnowsName(entry.knowsName);
```

- [ ] **Step 4: Compile + dry-test in Editor**

Compile in Editor. No errors. Manual save round-trip later (Task 12). For now, just verify the field is there.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs
git commit -m "feat(relation): persist KnowsName in save data"
```

---

## Task 4 — Add `AccentColor` to `CharacterArchetype`

**Files:**
- Modify: `Assets/Scripts/Character/Archetype/CharacterArchetype.cs`

- [ ] **Step 1: Add the SerializeField + getter at the end of the Identity block**

Open `Assets/Scripts/Character/Archetype/CharacterArchetype.cs`. After the Identity block (line 19, after `public FootSurfaceType DefaultFootSurface => _defaultFootSurface;`), insert a new block:

```csharp
// ── Visual identity ─────────────────────────────────────────────
[Header("Visual identity")]
[Tooltip("Default accent colour for this archetype — used as the name-strip background on speech bubbles. Per-character override via CharacterProfileSaveData.")]
[SerializeField] private Color _accentColor = new Color(0.78f, 0.48f, 0.23f);  // warm orange fallback

public Color AccentColor => _accentColor;
```

- [ ] **Step 2: Compile**

In Unity Editor: `Ctrl+R`. No errors expected. The new field shows up on every existing `CharacterArchetype.asset` as the default orange.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Character/Archetype/CharacterArchetype.cs
git commit -m "feat(archetype): add AccentColor field with warm-orange default"
```

---

## Task 5 — `Character.AccentColor` accessor + `NetworkVariable<Color32>` + override resolver

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs`

- [ ] **Step 1: Locate the `Character` class fields region**

Open `Assets/Scripts/Character/Character.cs`. Find a spot near the top of the class (after archetype field, before the subsystem references) where to add the new fields. If unsure, search for `_archetype` and add the new block immediately after the archetype-related members.

- [ ] **Step 2: Add the NetworkVariable + override field + property**

Insert (adjust placement to match existing field grouping):

```csharp
// ── Visual identity ─────────────────────────────────────────────
private readonly NetworkVariable<Color32> _accentColorNet = new NetworkVariable<Color32>(
    new Color32(199, 122, 58, 255), // matches CharacterArchetype default
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server);

private bool _hasAccentOverride;
private Color _accentOverride;

/// <summary>
/// Speech-bubble accent colour for this character. Resolves to:
/// (a) the per-character override if set (via SetAccentColor / save data restore),
/// (b) else the archetype default. Replicated to all clients via NetworkVariable.
/// </summary>
public Color AccentColor => (Color)_accentColorNet.Value;
```

- [ ] **Step 3: Initialise the NetworkVariable server-side on spawn**

Find `OnNetworkSpawn` in `Character.cs`. Inside the `if (IsServer)` block, add an initial write from the archetype default (so the value isn't stuck at the field's literal default if the archetype is something else):

```csharp
if (IsServer && _archetype != null)
{
    _accentColorNet.Value = (Color32)_archetype.AccentColor;
}
```

(If there's no `OnNetworkSpawn` override yet, add one following the pattern from neighbouring NetworkBehaviours.)

- [ ] **Step 4: Add the setter**

After the property:

```csharp
/// <summary>
/// Server-only setter — overwrites the replicated accent colour and flags the override.
/// Per-character cosmetic UIs must route through a ServerRpc that calls this on the server side.
/// </summary>
public void SetAccentColor(Color color)
{
    if (!IsServer)
    {
        Debug.LogWarning($"<color=orange>[Character]</color> SetAccentColor called on non-server peer for '{CharacterName}'. Ignored.");
        return;
    }
    _hasAccentOverride = true;
    _accentOverride = color;
    _accentColorNet.Value = (Color32)color;
}
```

- [ ] **Step 5: Compile**

`Ctrl+R` in Editor. Errors here usually mean `NetworkVariable<Color32>` isn't allowed because `Color32` is `struct` but might need an unmanaged constraint; `Color32` is `[StructLayout(LayoutKind.Sequential)]` and blittable, so it should compile. If not, fall back to `NetworkVariable<int>` packing RGBA bytes — but try `Color32` first.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(character): replicate AccentColor via NetworkVariable<Color32>"
```

---

## Task 6 — Save round-trip for accent-colour override

**Files:**
- Modify: `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs`
- Modify: `Assets/Scripts/Character/Character.cs` (Serialize/Deserialize that already lives on Character — find it during the task)

- [ ] **Step 1: Add the fields to the save data**

Open `Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs`. Add to the serializable struct/class (alongside existing fields like `CharacterName`, `Archetype`):

```csharp
public Color AccentColorOverride;
public bool HasAccentColorOverride;
```

- [ ] **Step 2: Wire export/import in `Character`'s save-data hooks**

In `Character.cs`, find where `CharacterProfileSaveData` is consumed (search for `CharacterProfileSaveData` — there's a typed `ICharacterSaveData<CharacterProfileSaveData>` impl somewhere, likely in `Character.cs` itself or a sibling). On Serialize, write:

```csharp
data.AccentColorOverride = _accentOverride;
data.HasAccentColorOverride = _hasAccentOverride;
```

On Deserialize (server-side path):

```csharp
if (data.HasAccentColorOverride)
{
    _hasAccentOverride = true;
    _accentOverride = data.AccentColorOverride;
    if (IsServer)
        _accentColorNet.Value = (Color32)data.AccentColorOverride;
}
```

- [ ] **Step 3: Compile + commit**

`Ctrl+R`. No errors.

```bash
git add Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs Assets/Scripts/Character/Character.cs
git commit -m "feat(persistence): round-trip per-character AccentColor override"
```

---

## Task 7 — EditMode test: `Relationship.KnowsName` defaults + setter

**Files:**
- Create: `Assets/Tests/EditMode/CharacterRelation/RelationshipKnowsNameTests.cs`
- Create: `Assets/Tests/EditMode/CharacterRelation/CharacterRelation.Tests.asmdef`

> **Note:** `Relationship` lives in the default `Assembly-CSharp` and has no Pure subset. To test it, the asmdef must reference Assembly-CSharp. The existing `Hunger.Tests.asmdef` / `Orders.Tests.asmdef` deliberately don't (they test pure subsets). The simplest path: model the asmdef on `WagesAndPerformance.Tests.asmdef` but reference `Assembly-CSharp` instead of a Pure assembly. If the implementer hits assembly-reference issues, fall back to a manual verification step (set/get + save round-trip in Editor) and skip this task.

- [ ] **Step 1: Create the asmdef**

Create `Assets/Tests/EditMode/CharacterRelation/CharacterRelation.Tests.asmdef` with content:

```json
{
    "name": "CharacterRelation.Tests",
    "rootNamespace": "MWI.Tests",
    "references": [
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner",
        "Assembly-CSharp"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Write the failing tests**

Create `Assets/Tests/EditMode/CharacterRelation/RelationshipKnowsNameTests.cs`:

```csharp
using NUnit.Framework;

namespace MWI.Tests.CharacterRelation
{
    public class RelationshipKnowsNameTests
    {
        [Test]
        public void NewRelationship_KnowsName_DefaultsFalse()
        {
            var rel = new Relationship(null, null);
            Assert.IsFalse(rel.KnowsName, "Fresh Relationship must default KnowsName to false.");
        }

        [Test]
        public void SetKnowsName_True_FlipsFlag()
        {
            var rel = new Relationship(null, null);
            rel.SetKnowsName(true);
            Assert.IsTrue(rel.KnowsName);
        }

        [Test]
        public void SetKnowsName_False_ClearsFlag()
        {
            var rel = new Relationship(null, null);
            rel.SetKnowsName(true);
            rel.SetKnowsName(false);
            Assert.IsFalse(rel.KnowsName);
        }

        [Test]
        public void KnowsName_IndependentFromHasMet()
        {
            var rel = new Relationship(null, null);
            rel.SetAsMet();
            Assert.IsFalse(rel.KnowsName, "HasMet=true must not auto-flip KnowsName.");
            rel.SetKnowsName(true);
            rel.SetAsNotMet();
            Assert.IsTrue(rel.KnowsName, "SetAsNotMet must not auto-flip KnowsName.");
        }
    }
}
```

Note: `Relationship`'s constructor stores `_character` and emits `Debug.Log` calls that read `_character.CharacterName` in `SetKnowsName`. Passing `null` for both characters means the `Debug.Log` line in `SetKnowsName` will NRE. **Fix by guarding the log in `Relationship.SetKnowsName`** — wrap the `Debug.Log` line in `if (_character != null && _relatedCharacter != null)`. This is defensible: nothing else in `Relationship` blows up with null references on the data-only path, so it's an existing constraint relaxation. Adjust Task 1's `SetKnowsName` accordingly before running tests.

Revised `SetKnowsName` body:

```csharp
public void SetKnowsName(bool value)
{
    if (_knowsName == value) return;
    _knowsName = value;
    if (_character != null && _relatedCharacter != null)
        Debug.Log($"<color=cyan>[Relation KnowsName]</color> {_character.CharacterName} -> {_relatedCharacter.CharacterName} : KnowsName = {value}");
}
```

Re-edit Task 1's body to match — or stage this as a follow-up commit. The clearest path: apply the guard now in `Relationship.cs`, amend or restate Task 1's listing in your local memory.

- [ ] **Step 3: Run the tests + verify they pass**

In Unity: Window → General → Test Runner → EditMode tab → run `CharacterRelation.Tests`. Expected: 4 tests pass.

If the asmdef setup blocks compilation: drop this task, document a manual save-round-trip verification step in Task 12 instead.

- [ ] **Step 4: Commit**

```bash
git add Assets/Tests/EditMode/CharacterRelation/ Assets/Scripts/Character/CharacterRelation/Relationship.cs
git commit -m "test(relation): cover KnowsName default/setter/independence"
```

---

## Task 8 — Restructure `SpeechBubbleInstance_Prefab`

**Files:**
- Modify (manually in Unity Editor): `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab`

> **Manual prefab work.** Either use the Unity Editor directly (open the prefab, drag children) or drive via MCP `assets-prefab-open` + `gameobject-create` / `gameobject-component-add`. The Editor route is faster for visual checks; the MCP route is faster for batch ops. Pick what fits.

- [ ] **Step 1: Open the prefab + survey current children**

In Unity Editor: open `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` (double-click). The current root has:
- `CanvasGroup`, `RectTransform`, `SpeechBubbleInstance` script
- A child Canvas hosting `Text_Speech` (TextMeshProUGUI)
- A `SeparatorLine` child (Image, ~60% width)

- [ ] **Step 2: Add a `NameStrip` child at the top of the layout**

Inside the root, **before** the existing body Canvas / Text_Speech, create a new GameObject `NameStrip`:
- Add `RectTransform` (auto)
- Add `Image` component — this will be tinted at runtime; set color to a neutral white at authoring so the override is visible.
- Add a `LayoutElement` if needed (preferred height ~22px).
- Inside `NameStrip`, add a `Text_Name` child with `TextMeshProUGUI` — font 11pt bold, white, left-aligned. Padding 8/4.

- [ ] **Step 3: Add the `Tail` child anchored to the bottom-centre of the body**

Inside the body panel (the existing Canvas/Image hosting `Text_Speech`), add a new GameObject `Tail`:
- `RectTransform` anchored to `(0.5, 0)` (bottom centre)
- Width 16, Height 12, Pivot `(0.5, 1)` so it hangs *below* the bubble body.
- Add an `Image` component. For a placeholder triangle, leave the sprite null and set color to the body's dark translucent — Unity will draw a rectangle, visually wrong but functional. **Replace sprite later** with an actual downward-triangle sprite (flagged in spec §8).
- Parent the Tail directly under the body panel so it inherits the dark colour.

- [ ] **Step 4: Add a root-level `VerticalLayoutGroup` + `ContentSizeFitter`**

On the prefab root, add:
- `VerticalLayoutGroup` (controlWidth: false, controlHeight: true, childForceExpandWidth: true, childForceExpandHeight: false)
- `ContentSizeFitter` (Vertical Fit: PreferredSize)

This stacks NameStrip above Body and sizes the root accordingly.

- [ ] **Step 5: Delete the `SeparatorLine` child**

Right-click `SeparatorLine` → Delete. Confirm.

- [ ] **Step 6: Save the prefab**

`Ctrl+S` in Unity, or via MCP `assets-prefab-save`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab
git commit -m "feat(prefab): add NameStrip+Tail to speech bubble, drop SeparatorLine"
```

---

## Task 9 — Refactor `SpeechBubbleInstance.cs`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs`

- [ ] **Step 1: Add new SerializeFields next to existing `_textElement`**

In `SpeechBubbleInstance.cs` near the existing SerializeFields (line 21 area), replace the `_separatorLine` line entirely and add the new fields:

```csharp
[SerializeField] private TextMeshProUGUI _textElement;
[SerializeField] private TextMeshProUGUI _nameText;
[SerializeField] private Image _nameStripBackground;
[SerializeField] private GameObject _tailRoot;
```

(Drop the `[SerializeField] private GameObject _separatorLine;` line.)

- [ ] **Step 2: Add the two new methods**

After the existing public API block (around line 124, after `SetStackOffsetPx`), add:

```csharp
/// <summary>
/// Sets the speaker-specific visuals: accent colour on the name strip + display name.
/// Called by SpeechBubbleStack right after Setup/SetupScripted.
/// </summary>
public void SetSpeakerDisplay(Color accent, string displayName)
{
    if (_nameStripBackground != null) _nameStripBackground.color = accent;
    if (_nameText != null) _nameText.text = displayName;
}

/// <summary>
/// Toggles the tail visibility. Only the newest bubble in a stack should have its tail visible.
/// Called by SpeechBubbleStack on push (new bubble = true, previously-newest = false) and on remove.
/// </summary>
public void SetIsNewest(bool isNewest)
{
    if (_tailRoot != null) _tailRoot.SetActive(isNewest);
}
```

- [ ] **Step 3: Delete the dead separator code**

Remove the entire `SetSeparatorVisible(bool visible)` method (around line 257-261).

- [ ] **Step 4: Wire the SerializeField references on the prefab**

In Unity Editor: re-open `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab`. On the root GameObject's `SpeechBubbleInstance` script component:
- Drag the `Text_Name` TMP into `_nameText`
- Drag the `NameStrip` Image into `_nameStripBackground`
- Drag the `Tail` GameObject (the parent of the triangle Image) into `_tailRoot`

Save the prefab.

- [ ] **Step 5: Compile + visual sanity check**

`Ctrl+R`. The Editor compiles. Open the prefab — no missing references on the script component.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Character/CharacterSpeech/SpeechBubbleInstance.cs Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab
git commit -m "feat(speech): SetSpeakerDisplay + SetIsNewest; drop separator"
```

---

## Task 10 — Refactor `SpeechBubbleStack.cs`

**Files:**
- Modify: `Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs`

> Note: A using directive (`using Unity.Netcode;`) + an updated class XML doc-comment were added earlier in this branch (pre-spec). Keep both — they are consistent with this task.

- [ ] **Step 1: Replace `_proximityRadius` with linear-fade fields**

In the SerializeField region (around line 27-32), replace:

```csharp
[SerializeField] private float _proximityRadius = 25f;
```

with:

```csharp
[Header("Distance fade (matches UI_RemoteActionIndicator)")]
[Tooltip("World units. ≤ FadeStart = full opacity. Local-player stack skips this entirely.")]
[SerializeField, Range(0f, 50f)] private float _fadeStartDistance = 12f;
[SerializeField, Range(1f, 200f)] private float _fadeEndDistance = 30f;
```

- [ ] **Step 2: Add local-player tri-state cache fields**

Near the other private fields (around line 35-43), add:

```csharp
// Local-player detection — cached on first successful resolve. Tri-state via the resolved flag.
private bool _isLocalPlayer;
private bool _isLocalPlayerResolved;
```

- [ ] **Step 3: Add `IsLocalPlayerStack()` helper**

After `OnTriggerExit` (around line 154), add:

```csharp
/// <summary>
/// Lazy-resolves whether this stack belongs to the local player's Character.
/// Returns false while NetworkManager / LocalClient / PlayerObject aren't ready yet;
/// re-tries each call until it can answer once, then caches the resolved value.
/// Mirrors RemoteActionIndicatorLayer.IsLocalPlayer.
/// </summary>
private bool IsLocalPlayerStack()
{
    if (_isLocalPlayerResolved) return _isLocalPlayer;
    try
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null) return false;
        var localObj = nm.LocalClient.PlayerObject;
        if (localObj == null) return false;
        if (OwnerRoot == null) return false;
        var ownerChar = OwnerRoot.GetComponent<Character>();
        if (ownerChar == null) ownerChar = OwnerRoot.GetComponentInParent<Character>();
        _isLocalPlayer = ownerChar != null && ownerChar.NetworkObject == localObj;
        _isLocalPlayerResolved = true;
        return _isLocalPlayer;
    }
    catch (Exception e)
    {
        Debug.LogException(e);
        return false;
    }
}
```

- [ ] **Step 4: Replace the proximity check in `Update`**

In `Update` (line 71-116), the current block computes `inRange = distSq <= _proximityRadius * _proximityRadius;`. Replace it with the linear-fade math:

Locate the block from line 86 (`if (_wrapperGroup == null || _bubbles.Count == 0) return;`) to line 110 (the `MoveTowards` line) and replace from after the null guard onward:

```csharp
if (_wrapperGroup == null || _bubbles.Count == 0) return;

// Local-player stack: always fully opaque — skip distance + on-screen gates.
if (IsLocalPlayerStack())
{
    _wrapperGroup.alpha = Mathf.MoveTowards(_wrapperGroup.alpha, 1f, _fadeSpeed * Time.unscaledDeltaTime);
    return;
}

var local = HUDSpeechBubbleLayer.Local;
if (local == null || local.LocalPlayerAnchor == null)
{
    _wrapperGroup.alpha = Mathf.MoveTowards(_wrapperGroup.alpha, 0f, _fadeSpeed * Time.unscaledDeltaTime);
    return;
}

// 2D X-Z distance — Y doesn't matter for "is the other character close to me".
Vector3 speakerPos = OwnerRoot != null ? OwnerRoot.position : transform.position;
Vector3 a = local.LocalPlayerAnchor.position; a.y = 0f;
Vector3 b = speakerPos; b.y = 0f;
float d = Vector3.Distance(a, b);

float distanceAlpha;
if (d <= _fadeStartDistance)      distanceAlpha = 1f;
else if (d >= _fadeEndDistance)   distanceAlpha = 0f;
else                              distanceAlpha = 1f - (d - _fadeStartDistance) / Mathf.Max(0.001f, _fadeEndDistance - _fadeStartDistance);

bool anyOnScreen = false;
for (int i = 0; i < _bubbles.Count; i++)
{
    if (_bubbles[i] != null && !_bubbles[i].IsOffScreen) { anyOnScreen = true; break; }
}

float targetAlpha = anyOnScreen ? distanceAlpha : 0f;
_wrapperGroup.alpha = Mathf.MoveTowards(_wrapperGroup.alpha, targetAlpha, _fadeSpeed * Time.unscaledDeltaTime);
```

- [ ] **Step 5: Add `ResolveSpeakerDisplay` helper**

After `IsLocalPlayerStack` (or anywhere in the private helper region), add:

```csharp
/// <summary>
/// Computes the (accent colour, display name) pair for this stack's speaker.
/// Display name resolves via local player's CharacterRelation.GetRelationshipWith(speaker).KnowsName,
/// falling back to "???" when KnowsName is false (or no relationship entry exists).
/// </summary>
private (Color accent, string displayName) ResolveSpeakerDisplay()
{
    var speaker = OwnerRoot != null ? OwnerRoot.GetComponent<Character>() : null;
    if (speaker == null && OwnerRoot != null) speaker = OwnerRoot.GetComponentInParent<Character>();

    Color accent = speaker != null ? speaker.AccentColor : new Color(0.78f, 0.48f, 0.23f);

    string displayName = "???";
    if (speaker != null)
    {
        bool isLocalPlayer = IsLocalPlayerStack();
        if (isLocalPlayer)
        {
            displayName = speaker.CharacterName;
        }
        else
        {
            try
            {
                var nm = NetworkManager.Singleton;
                var localObj = nm?.LocalClient?.PlayerObject;
                var localChar = localObj != null ? localObj.GetComponent<Character>() : null;
                var relSys = localChar != null ? localChar.GetComponentInChildren<CharacterRelation>() : null;
                var rel = relSys != null ? relSys.GetRelationshipWith(speaker) : null;
                if (rel != null && rel.KnowsName)
                    displayName = speaker.CharacterName;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    return (accent, displayName);
}
```

- [ ] **Step 6: Wire `ResolveSpeakerDisplay` into both `PushBubble` and `PushScriptedBubble`**

In `PushBubble` (line 158-189), after `instance = Instantiate(...);` and `instance.SetSpeakerAnchor(...)` / `instance.SetCamera(...)` (around line 168-170):

```csharp
var (accent, displayName) = ResolveSpeakerDisplay();
instance.SetSpeakerDisplay(accent, displayName);

// Tail handover: previously-newest (index 0 of the soon-to-grow list) loses its tail.
if (_bubbles.Count > 0) _bubbles[0].SetIsNewest(false);
```

Then after `_bubbles.Insert(0, instance);` (line 178):

```csharp
instance.SetIsNewest(true);
```

Mirror the same insertions in `PushScriptedBubble` (around line 199-216).

- [ ] **Step 7: Update `RemoveBubble` to hand the tail to the new newest**

In `RemoveBubble` (line 362-370), after `_bubbles.Remove(instance);`:

```csharp
if (_bubbles.Count > 0) _bubbles[0].SetIsNewest(true);
```

- [ ] **Step 8: Drop dead separator code**

Delete the `UpdateSeparatorVisibility` method (line 390-397). Remove every call site (search for `UpdateSeparatorVisibility` and delete the lines — should appear in `PushBubble`, `PushScriptedBubble`, `RemoveBubble`).

- [ ] **Step 9: Compile + commit**

`Ctrl+R`. Expected: clean compile.

```bash
git add Assets/Scripts/Character/CharacterSpeech/SpeechBubbleStack.cs
git commit -m "feat(speech): linear fade + local-player exemption + name+tail wiring; drop separator + proximity radius"
```

---

## Task 11 — Author default accent colours on existing `CharacterArchetype` assets

**Files:**
- Modify (manually in Unity Editor or via MCP): each existing `*.asset` under `Assets/Resources/Data/CharacterArchetypes/` (or wherever archetypes live).

- [ ] **Step 1: Locate every CharacterArchetype asset**

In Unity Editor's Project window, search `t:CharacterArchetype`. List every asset.

- [ ] **Step 2: Set a distinctive `_accentColor` on each**

For each archetype, pick a visually distinct accent — examples:
- Villager archetype → warm orange `#c97a3a`
- Soldier archetype → muted blue `#4a7dc4`
- Merchant archetype → mustard yellow `#c9a83a`
- Wild / Beast → desaturated grey-brown `#6a4a32`

Save each asset (`Ctrl+S` or right-click → Apply).

This is **designer authoring** — pick what fits the project's existing palette. If unsure, leave the default warm-orange and revisit during a visual-polish pass.

- [ ] **Step 3: Commit**

```bash
git add Assets/Resources/Data/CharacterArchetypes/
git commit -m "feat(archetype): author default accent colours per archetype"
```

---

## Task 12 — Documentation pass (SKILL + wiki + agent)

**Files:**
- Modify: `.agent/skills/speech-system/SKILL.md`
- Modify: `wiki/systems/character-speech.md`
- Modify: `wiki/systems/character-relation.md`
- Modify: `wiki/systems/player-hud.md`
- Modify (if needed): `.claude/agents/character-social-architect.md`

- [ ] **Step 1: Update `.agent/skills/speech-system/SKILL.md`**

Add a change-log entry at the top (or at the end depending on convention) — read the file first to find the right spot:
- Add `SetSpeakerDisplay(Color accent, string displayName)` and `SetIsNewest(bool isNewest)` to the `SpeechBubbleInstance` public-method table.
- Replace the `_proximityRadius` row in the `SpeechBubbleStack` SerializeField table with `_fadeStartDistance` + `_fadeEndDistance`.
- Add the local-player exemption rule next to the fade description.
- Remove the `SetSeparatorVisible(bool visible)` row.
- Mention the `Relationship.KnowsName → "???"` lookup in the Data-Flow section.

- [ ] **Step 2: Update `wiki/systems/character-speech.md`**

- Bump `updated:` to `2026-05-19`.
- Append change-log line: `- 2026-05-19 — Refined-Habbo visuals (name strip + tail), linear distance fade matching action-indicator, KnowsName-aware name display, dead-code sweep. — claude`
- Refresh §Data flow (rename "proximity gate" to "linear distance fade"; add the `ResolveSpeakerDisplay` step).
- Strike the resolved item from §Open questions (per-archetype proximity radius if it was listed).
- Add `[[character-relation]]` to `depends_on` if not already there.

- [ ] **Step 3: Update `wiki/systems/character-relation.md`**

- Bump `updated:` to `2026-05-19`.
- Document `KnowsName` semantics: separate from `HasMet`, drives display-name fallback in [[character-speech]].
- Add `KnowsName` to whatever Public-API / state table the page uses.
- Append change-log line: `- 2026-05-19 — Added KnowsName bool (separate from HasMet) — drives speech-bubble "???" display when local player hasn't been told the speaker's name. — claude`
- Add `[[character-speech]]` to `depended_on_by`.

- [ ] **Step 4: Update `wiki/systems/player-hud.md`**

- Bump `updated:`.
- Change-log line: `- 2026-05-19 — Speech-bubble distance fade now mirrors UI_RemoteActionIndicator (12u → 30u linear, X-Z plane, local-player exemption). Unified HUD fade convention. — claude`

- [ ] **Step 5: Update `.claude/agents/character-social-architect.md` if domain-relevant**

Read the agent descriptor. If the description block mentions `CharacterRelation` API in detail, add the `KnowsName` field. If it's vague ("compatibility-based relationships"), no update needed.

- [ ] **Step 6: Commit**

```bash
git add .agent/skills/speech-system/SKILL.md wiki/systems/character-speech.md wiki/systems/character-relation.md wiki/systems/player-hud.md
# also stage .claude/agents/character-social-architect.md if changed
git commit -m "docs: speech-bubble rework + KnowsName + linear-fade parity"
```

---

## Task 13 — Single-player manual verification (Editor)

**Files:** none (in-Editor verification only)

- [ ] **Step 1: Open a scene with multiple characters**

In Unity Editor: open the main gameplay scene (or use DevMode to spawn 3-4 characters of different archetypes in close proximity).

- [ ] **Step 2: Enter Play Mode and trigger speech**

Use the chat bar or `/say` / a debug spawn that calls `character.Speech.Say("Hello there", 5f, 0.04f)` on each spawned character (and the player).

Expected:
- Player's own speech bubble shows the player's name, accent colour, and tail. **Stays at full opacity** when the camera moves.
- Each NPC's bubble shows their name (or `???` if their relationship is `KnowsName == false`) in the archetype's accent colour. Newest bubble has the tail.
- Stacking still works — each call to `Say` adds another bubble below the previous; older bubbles lose their tail when a new one appears.

- [ ] **Step 3: Walk the player away from a speaker**

Move the player character toward and away from one NPC. At ≤12u distance, the NPC's bubble stack is fully visible. From 12u → 30u, the wrapper alpha gradients linearly. Past 30u, the bubble is invisible.

- [ ] **Step 4: Force one relationship to `KnowsName = true` and verify**

In the Editor: pause Play Mode, find one NPC's `CharacterRelation` component on the player, locate that relationship in the inspector, flip the `KnowsName` field (via the inspector or a debug call). Resume. The NPC's bubble now shows the real name instead of `???`.

Note: if no inspector toggle exists, add a quick debug call from the DevMode panel or a temporary scratch script that calls `player.CharacterRelation.GetRelationshipWith(npc).SetKnowsName(true)`.

- [ ] **Step 5: Save round-trip**

Trigger a save (bed checkpoint / portal gate / debug save key). Quit Play Mode. Re-enter Play Mode and confirm the `KnowsName = true` and any AccentColor override survived. Real name still shows.

- [ ] **Step 6: Commit verification notes if useful**

If you discovered tuning tweaks (e.g. tail position offset, name strip padding), apply them and commit:

```bash
git add Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab
git commit -m "tune(speech): polish from in-editor verification"
```

Otherwise no commit needed.

---

## Task 14 — Late-joiner repro (host + client, two-process)

**Files:** none (live verification per spec §6).

- [ ] **Step 1: Build two test runs**

Either run two Unity Editor sessions (clone the project + open both) or build the player and connect a second instance. The "two test runs" pattern is described in the multiplayer SKILL.md.

- [ ] **Step 2: Host starts, two characters meet**

Host launches. Drive two NPCs (or the host's player + an NPC) through the introduction path until both have `HasMet = true` and `KnowsName = true` for each other.

- [ ] **Step 3: Client joins fresh**

Second instance joins as a client.

- [ ] **Step 4: Verify on the joining client**

From the joining client's perspective:
1. Walk near a "known" NPC (one the client's player has met by name): bubble shows the real name + correct accent.
2. Walk near a "not met" NPC: bubble shows `???` + correct accent.
3. Trigger a server-side `SetKnowsName(true)` on a previously-unknown NPC (DevMode shortcut or a quick debug ServerRpc). The client's bubble updates to the real name within one frame.

- [ ] **Step 5: Verify accent override replication**

On the host, set a custom accent on an NPC via `SetAccentColor`. The joining client's bubble shows the new colour immediately (NetworkVariable change propagates).

- [ ] **Step 6: Update SKILL.md with the late-joiner outcome**

Per rule #19b: commits and SKILL.md updates must state that the late-joiner repro was performed. Add a line to `.agent/skills/speech-system/SKILL.md` § Network Considerations:

> Late-joiner repro performed 2026-05-19 (host+client): `Relationship.KnowsName` survives NetworkList replay; `Character.AccentColor` NetworkVariable<Color32> propagates correctly; bubbles show real name only when the joining client's `KnowsName == true`.

- [ ] **Step 7: Final commit**

```bash
git add .agent/skills/speech-system/SKILL.md
git commit -m "docs(speech): record late-joiner repro for KnowsName + AccentColor"
```

---

## Self-Review Checklist

(Run this after writing — see writing-plans skill for what "self-review" means.)

### Spec coverage

| Spec section | Covered by task(s) |
|---|---|
| §3 — Name strip on every bubble | Task 8 (prefab), Task 9 (SetSpeakerDisplay), Task 10 (ResolveSpeakerDisplay → SetSpeakerDisplay each push) |
| §3 — Tail on newest bubble only | Task 8 (prefab), Task 9 (SetIsNewest), Task 10 (tail handover in PushBubble/PushScriptedBubble/RemoveBubble) |
| §3 — `???` for unknown speakers | Task 1 (KnowsName field), Task 10 (ResolveSpeakerDisplay) |
| §3 — Accent colour preserved on `???` | Task 10 (ResolveSpeakerDisplay always returns the speaker's accent, regardless of name fallback) |
| §3 — Linear fade 12u → 30u + local-player exemption | Task 10 (Update branch) |
| §3 — Separator line removed | Task 8 (prefab), Task 9 (drop SetSeparatorVisible), Task 10 (drop UpdateSeparatorVisibility) |
| §4.1 — `Relationship.KnowsName` + sync + save | Tasks 1, 2, 3 |
| §4.2 — `CharacterArchetype.AccentColor` | Task 4 |
| §4.3 — `Character.AccentColor` + NetworkVariable | Task 5 |
| §4.3 — `CharacterProfileSaveData.AccentColorOverride` | Task 6 |
| §5 — Prefab restructure | Task 8 |
| §5 — `SpeechBubbleInstance.cs` changes | Task 9 |
| §5 — `SpeechBubbleStack.cs` changes | Task 10 |
| §5 — Wiki + SKILL doc updates | Task 12 |
| §6 — Multiplayer audit (late-joiner repro) | Task 14 |
| §9 done definition (1) — name strip on every bubble | Task 13 step 2 |
| §9 done definition (2) — `???` for unknown | Task 13 step 2, Task 14 step 4 |
| §9 done definition (3) — tail handover | Task 13 step 2 |
| §9 done definition (4) — linear fade | Task 13 step 3 |
| §9 done definition (5) — Habbo cross-character push | Task 13 step 2 (verifies stacking still works) |
| §9 done definition (6) — late-joiner | Task 14 |
| §9 done definition (7) — docs current | Task 12 |
| §9 done definition (8) — no `_separatorLine` remaining | Tasks 8 + 9 (delete from both prefab and code) |

### Placeholder scan

- No "TBD" / "TODO" / "fill in details" in any task body. ✓
- Every code step has a full code block. ✓
- Every command step has the exact command. ✓
- One known caveat: Task 11 (designer authoring of per-archetype colours) is necessarily subjective — the task lists a 4-archetype palette as a starting point and explicitly defers to "what fits the project's existing palette." That's authoring judgment, not a placeholder.

### Type consistency

- `SetSpeakerDisplay(Color accent, string displayName)` referenced identically in Task 9 (defined) and Task 10 (called). ✓
- `SetIsNewest(bool isNewest)` referenced identically in Task 9 (defined) and Task 10 (called from `PushBubble` / `PushScriptedBubble` / `RemoveBubble`). ✓
- `_fadeStartDistance` / `_fadeEndDistance` field names consistent across Task 10 introduction + Update math + spec §3. ✓
- `Relationship.KnowsName` getter + `SetKnowsName(bool)` setter shape used consistently across Tasks 1, 2, 3, 7, 10, 13, 14. ✓
- `Character.AccentColor` getter + `SetAccentColor(Color)` setter shape consistent across Tasks 5, 6, 10, 14. ✓
- `_accentColorNet : NetworkVariable<Color32>` field name consistent across Tasks 5, 6. ✓
