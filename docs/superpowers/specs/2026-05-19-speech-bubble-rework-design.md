# Speech Bubble Rework — Design Spec

**Date:** 2026-05-19
**Topic:** Refresh the in-bubble visuals (coloured name strip + tail-to-speaker + `???` for unknown speakers) and refactor the matching code. Stacking + RPC + dialogue behaviour stays as-is.
**Related system:** `.agent/skills/speech-system/SKILL.md`, [wiki/systems/character-speech.md](../../../wiki/systems/character-speech.md), [wiki/systems/character-relation.md](../../../wiki/systems/character-relation.md)
**Builds on:** `docs/superpowers/specs/2026-04-20-hud-speech-bubbles-design.md` (HUD-space rewrite; stacking + projection preserved).
**Touches systems:** [[character-speech]], [[character-relation]] (new `KnowsName` bool), [[character-archetype]] (new `AccentColor`), [[ui-hud]] (mirrors `UI_RemoteActionIndicator` fade pattern), [[character-persistence]] (override storage).

---

## 1. Problem

Current speech bubbles are visually identical for every speaker — same dark translucent rectangle, white text, no speaker name shown above the bubble itself. The user can't tell at a glance *who* said the line above another character's head without inferring from position. Distance fade is binary (in/out at 25u) instead of the smooth linear gradient the action-indicator HUD shipped on 2026-05-18. The relationship-aware "you don't actually know this NPC's name" affordance is missing entirely.

## 2. Goal

Apply a refined-Habbo visual to each existing bubble: a coloured **name strip** on top (using the speaker's accent colour, showing either the speaker's name or `???` based on the local player's `Relationship.KnowsName` flag) + a downward **tail** on the newest bubble in the stack. Convert the proximity gate to a linear distance fade matching `UI_RemoteActionIndicator` (12u full → 30u invisible, X-Z plane, local-player exemption). Refactor dead code along the way.

Explicit **non-goals** for this spec:
- Any change to stacking logic (Habbo cross-character push, vertical stack, cap of 5, dismiss rules).
- Any change to RPC routing, dialogue API (`Say` / `SayScripted` / `CloseSpeech` / `ResetSpeech`), or voice playback.
- Portraits, mood icons, shout/whisper variants, off-screen edge arrows, per-archetype proximity radii.
- A separate "introduction" UX — `KnowsName` becomes available as a flag; *how* it gets set (cinematic, dialogue option, on-talk handshake) is a follow-up.
- Replacing the four-class split (`CharacterSpeech` / `SpeechBubbleStack` / `SpeechBubbleInstance` / `HUDSpeechBubbleLayer`). The split stays.

## 3. Design defaults (confirmed with user)

| Concern | Value |
|---|---|
| Name strip placement | **Every bubble** in the stack carries its own name strip (self-identifying; tall stacks remain readable). |
| Tail placement | **Newest bubble only** (index 0). Previously-newest hides its tail when a new bubble is pushed. |
| Tail direction | Downward, attached to the bottom centre of the bubble body. Static (does not rotate to track the speaker if the bubble is offset). |
| Accent colour source | `CharacterArchetypeSO.AccentColor` default + per-character override stored in `CharacterProfileSaveData`. |
| `KnowsName` storage | New `bool` on `Relationship`, replicated through the same `CharacterRelationSyncData` channel `HasMet` uses, persisted in the same save export. |
| Unknown speaker display | Name text rendered as `???` (literal three question marks). **Accent colour still uses the speaker's real colour** — the player can see *that* a particular character is speaking, just not *who*. |
| Distance fade | Linear, `_fadeStartDistance = 12u` → `_fadeEndDistance = 30u`, X-Z plane (Y ignored). Smoothed via `Mathf.MoveTowards(_fadeSpeed * unscaledDeltaTime)` — kept from the HUD-space spec. |
| Local-player exemption | The local player's own stack skips the distance fade AND the on-screen gate — alpha always lerps to 1. Mirrors `UI_RemoteActionIndicator._isLocalPlayer`. |
| Bubble bottom-up order | Unchanged — index 0 is the **newest** bubble at the base (closest to the speaker's head). Index `Count - 1` is the oldest, at the top of the stack. |
| Separator line between bubbles | **Removed.** The name strip on each bubble becomes the visual boundary; the explicit `_separatorLine` GameObject is dropped along with its visibility code. |

## 4. Data model changes

### 4.1 `Relationship.KnowsName` (new)

Add a third state field to [Assets/Scripts/Character/CharacterRelation/Relationship.cs](../../../Assets/Scripts/Character/CharacterRelation/Relationship.cs):

```csharp
private bool _knowsName;
public bool KnowsName => _knowsName;
public void SetKnowsName(bool value) { _knowsName = value; /* fire OnRelationshipChanged */ }
```

Replication path: extend [CharacterRelation.cs](../../../Assets/Scripts/Character/CharacterRelation/CharacterRelation.cs):
- `CharacterRelationSyncData` gains a `public bool KnowsName;` field and its `NetworkSerialize`, `Equals`, and round-trip code path. The sync data already replicates `HasMet`, `RelationValue`, `RelationType` — this is one more field on the same channel.
- Save-data export (line ~369 area) includes `knowsName = rel.KnowsName` on each entry; import sets `existing.SetKnowsName(syncData.KnowsName)` (parallel to `SetAsMet`).

Default value: `false`. Becomes `true` only when an external system explicitly sets it (introduction cinematic, dialogue option — out of scope).

Migration: old saves with no `knowsName` field deserialize to `false` (default). This means existing relationships render as `???` after upgrade until the player re-meets the character or a follow-up cinematic seeds the bool. That's acceptable for this stage of dev.

### 4.2 `CharacterArchetypeSO.AccentColor` (new)

Add to the archetype SO (likely [Assets/Scripts/Character/Data/CharacterArchetypeSO.cs](../../../Assets/Scripts/Character/Data/CharacterArchetypeSO.cs); confirmed during implementation):

```csharp
[Header("Speech bubble")]
[SerializeField, Tooltip("Default accent colour for this archetype — used as the name-strip background on speech bubbles. Per-character override via CharacterProfileSaveData.")]
private Color _accentColor = new Color(0.78f, 0.48f, 0.23f);  // sensible warm fallback
public Color AccentColor => _accentColor;
```

Default value: warm orange so an unauthored archetype is still visibly accent-coloured. Each shipped archetype gets art-directed in a follow-up authoring pass.

### 4.3 `Character.AccentColor` accessor + override (new)

Add to [Assets/Scripts/Character/Character.cs](../../../Assets/Scripts/Character/Character.cs):

- Public read-only property `Color AccentColor { get; }` resolved from: **(a)** an explicit override stored in `CharacterProfileSaveData.AccentColorOverride` if set, **(b)** else the archetype's `_accentColor`.
- Override storage on `CharacterProfileSaveData`: `Color AccentColorOverride; bool HasAccentColorOverride;` (Unity's `Color` is a struct, so we need a paired bool — same pattern other override fields use).
- Setter `SetAccentColor(Color)` on Character marks the override field true and writes to the save data; players can use this from a (future) cosmetic UI. Out of scope to wire up that UI now — the spec only adds the data plumbing.

Replication: per-character override is read-during-bubble-construct on every client. Because `CharacterProfileSaveData` is the portable save (rule #20), the override is naturally available on the owning client via save round-trip. For non-owning clients to see the same override (and they must — the bubble's name strip must look the same on everyone's screen), the resolved `AccentColor` needs a server-authoritative replicated value. Implementation choice:

- **Option A (simple, recommended):** `Character` carries a `NetworkVariable<Color>` (or `NetworkVariable<Color32>` to stay blittable) populated server-side from save data. Clients read `Character.AccentColor` from the NetworkVariable. Archetype default fills the value before save data lands.
- **Option B (deferred):** route through the existing customization replication (if `ICharacterPartCustomization` already covers this kind of data). Skip if it doesn't.

The spec picks **Option A**. Late-joiner correctness: NetworkVariable<Color32> serializes via the standard ValueChanged channel; a fresh client gets the current value at spawn.

### 4.4 No new save schema for the bubble itself

Speech bubbles remain ephemeral UI — no replication, no save (unchanged from current).

## 5. Visual + code changes per file

### 5.1 `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` — restructure

Current root: `CanvasGroup` + `RectTransform` + body Image + `Text_Speech` TMP + `SeparatorLine` GameObject.

New child layout under the root RectTransform (vertical):

```
SpeechBubbleInstance (root)
├── CanvasGroup, RectTransform, SpeechBubbleInstance.cs
├── VerticalLayoutGroup (control width: false, control height: true, child force expand: width true)
├── ContentSizeFitter (Vertical = PreferredSize) — drives root height from name+body
├── NameStrip (child #1)
│   ├── Image (background — drives the accent colour at runtime)
│   ├── HorizontalLayoutGroup / padding 8/4
│   └── Text_Name (TextMeshProUGUI, 11pt bold, white)
└── BodyPanel (child #2)
    ├── Image (background — translucent dark, kept from current bubble)
    ├── VerticalLayoutGroup + ContentSizeFitter
    ├── Text_Speech (TextMeshProUGUI, 13pt, white, current settings preserved)
    └── Tail (child of BodyPanel, anchored bottom-centre)
        └── Image (downward triangle sprite, tinted to match body background)
```

Rules enforced by the existing speech-bubble gotcha list (rule #39 mirror):
- Root anchors stay `(0, 0)` (project absolute screen pixels — already a documented gotcha).
- `ContentSizeFitter` lives on the body panel for text wrap (preserved) AND a new one on the root for the vertical sum (name strip + body). Never on `Panel_Main_Background`-style fixed frames.

The old `SeparatorLine` GameObject is **deleted** from the prefab.

### 5.2 `SpeechBubbleInstance.cs`

New serialized fields:
- `[SerializeField] private Image _nameStripBackground;`
- `[SerializeField] private TextMeshProUGUI _nameText;`
- `[SerializeField] private Image _tailImage;`
- `[SerializeField] private GameObject _tailRoot;` — toggled active when `_isNewest == true`.

New runtime methods:
- `void SetSpeakerDisplay(Color accent, string displayName)` — sets `_nameStripBackground.color = accent`, `_tailImage.color = body.color` (kept in sync so tail matches body), `_nameText.text = displayName`. Called by the stack right after instantiation, with the resolved name (real name or `???`).
- `void SetIsNewest(bool isNewest)` — toggles `_tailRoot.SetActive(isNewest)`. Called by the stack on insertion + on the previously-newest at the same time.

Drop:
- `_separatorLine` field, `SetSeparatorVisible(bool)` method.

The existing `Setup` / `SetupScripted` / typing / entrance-exit / position-projection code is **unchanged**. The accent + name + tail wiring is additive.

### 5.3 `SpeechBubbleStack.cs`

Field changes:
- **Keep** `_separatorSpacingPx` (4px default). The strip-to-strip gap still needs spacing between stacked bubbles; the field name keeps describing what it does. Whether 4px reads well visually is flagged in §8 — adjust during implementation but don't remove.
- **Remove** `_proximityRadius`. Replace with `_fadeStartDistance = 12f` and `_fadeEndDistance = 30f` (mirrors `UI_RemoteActionIndicator` field names + ranges).

Method changes:
- `PushBubble` / `PushScriptedBubble`: after `Instantiate`, call `ResolveSpeakerDisplay(...)` to compute `(Color accent, string displayName)` and pass them to `instance.SetSpeakerDisplay(...)`. Then mark the previous index-0 bubble (if any) as no-longer-newest via `_bubbles[0].SetIsNewest(false)` *before* the `_bubbles.Insert(0, instance)`. After insert, call `instance.SetIsNewest(true)`.
- `RemoveBubble(SpeechBubbleInstance)`: after removal, if the bubble removed was at index 0 and the list is non-empty, call `_bubbles[0].SetIsNewest(true)` so the now-newest grows a tail.
- New `private (Color accent, string displayName) ResolveSpeakerDisplay()`: pulls accent from `Character.AccentColor` of `OwnerRoot.GetComponentInParent<Character>()`. For the display name, looks up the local player's `Character.CharacterRelation.GetRelationshipWith(speaker)?.KnowsName`. If `true` → speaker's real name; else → `"???"`.
- New `Update` distance-fade branch — the linear math from `UI_RemoteActionIndicator.ApplyDistanceFade`, plus local-player exemption:
  ```csharp
  if (IsLocalPlayerStack()) { _wrapperGroup.alpha = MoveTowards(...1f...); return; }
  // ... existing local + LocalPlayerAnchor null guard ...
  Vector3 a = local.LocalPlayerAnchor.position; a.y = 0f;
  Vector3 b = speakerPos; b.y = 0f;
  float d = Vector3.Distance(a, b);
  float distanceAlpha;
  if (d <= _fadeStartDistance) distanceAlpha = 1f;
  else if (d >= _fadeEndDistance) distanceAlpha = 0f;
  else distanceAlpha = 1f - (d - _fadeStartDistance) / Mathf.Max(0.001f, _fadeEndDistance - _fadeStartDistance);
  float targetAlpha = anyOnScreen ? distanceAlpha : 0f;
  _wrapperGroup.alpha = MoveTowards(...);
  ```
- New helper `bool IsLocalPlayerStack()` — caches a tri-state (Unknown / True / False). Resolves once `NetworkManager.Singleton.LocalClient.PlayerObject` is available + `OwnerRoot.GetComponentInParent<Character>().NetworkObject == localObj`. Lazy-resolves on every Update until first success (handles late-spawn race).

Drop:
- `UpdateSeparatorVisibility` and every call site.

### 5.4 `CharacterSpeech.cs`

No public-API change. The stack already receives `OwnerRoot` via `Init(Transform ownerRoot, MouthController mouthController)` — that transform is the root Character per the existing wiring (`CharacterSpeech.Start` passes `_character.transform`), so `OwnerRoot.GetComponent<Character>()` resolves in one hop. No signature change to `Init`.

### 5.5 Wiki + skill updates (per rules #28 + #29b)

After implementation:
- `.agent/skills/speech-system/SKILL.md` — change-log entry; document `SetSpeakerDisplay`, `SetIsNewest`, the linear-fade fields, the local-player exemption, and the `Relationship.KnowsName → "???"` lookup. Remove the dropped `SetSeparatorVisible` doc entry.
- [wiki/systems/character-speech.md](../../../wiki/systems/character-speech.md) — bump `updated:` to 2026-05-19; change-log line; refresh §Data-flow (mention `ResolveSpeakerDisplay` + the linear fade path); refresh §Open-questions to remove the resolved `_proximityRadius` per-archetype TODO if it's now redundant.
- [wiki/systems/character-relation.md](../../../wiki/systems/character-relation.md) — bump date; document `KnowsName` on the page (semantics, sync, save round-trip); add it to the Public-API table; add `[[character-speech]]` to `depended_on_by`.
- [wiki/systems/player-hud.md](../../../wiki/systems/player-hud.md) — change-log line noting "speech-bubble fade now mirrors `UI_RemoteActionIndicator` defaults (12u → 30u, X-Z plane, local-player exemption)".
- `.claude/agents/character-social-architect.md` — touch if the `CharacterRelation` description needs the new `KnowsName` flag (per rule #29).

## 6. Multiplayer audit (rule #19b — mandatory)

The new networked state on this rework:

| Field | Writer | Readers | Replication channel |
|---|---|---|---|
| `Relationship.KnowsName` | Server, via dialogue / cinematic / setter on owning character | All clients (need to render `???` per local-player perspective) | `CharacterRelationSyncData.KnowsName` field on the existing NetworkList sync path |
| `Character.AccentColor` (override) | Server, from save data (or owner via future cosmetic UI routed through ServerRpc) | All clients (need to render correct accent on remote speakers' bubbles) | `NetworkVariable<Color32>` on `Character` |

Late-joiner repro plan (must be done before claiming the feature done):
1. Host launches, two characters meet (`HasMet = true`, `KnowsName = true`).
2. A client joins fresh after both events.
3. From the joining client, the local player walks near a known NPC — bubble shows the real name + correct accent.
4. From the joining client, the local player walks near an NPC the player has *not* met — bubble shows `???` + correct accent.
5. Symmetric: host triggers a `SetKnowsName(true)` mid-session via debug action — joining client's bubble updates within one frame.

Symmetric checks per `wiki/gotchas/host-only-state-blindspot.md`:
- (1) Writer = server-only setters; (2) channel = NetworkList + NetworkVariable; (3) late-joiner = covered by NGO's value-change replay; (4) client pre-gate = none (`???` is the visual fallback, not a gate); (5) Awake registration race = `IsLocalPlayerStack()` lazy-resolves so a stack created before the LocalClient.PlayerObject is set still binds correctly; (6) proximity = X-Z linear fade, no inline `Vector3.Distance` math outside the documented gate.

## 7. Performance + UI notes

- The per-stack `Update` already runs every frame; the new branch adds one `Vector3.Distance` + one branch chain — well below any threshold worth gating.
- `IsLocalPlayerStack()` caches on first successful resolve; ongoing Updates skip the resolve path.
- `Color32` for the NetworkVariable keeps it blittable (4 bytes) — cheaper to replicate than `Color` (4 floats = 16 bytes).
- Name-strip and tail are static UGUI children — no dynamic Instantiate per bubble beyond the existing bubble prefab.
- No `Debug.Log` introduced in any hot path; if one is needed for the late-joiner repro, gate behind `NPCDebug.VerboseSpeech` or equivalent.

## 8. Open questions / flagged decisions

- **`_separatorSpacingPx` field fate.** Today it's a 4px vertical gap between stacked bubbles. With the new name-strip-on-every-bubble visual, the gap might read as either "natural spacing" or "annoying empty band." Decide during implementation by eyeballing the result; default to keeping 4px and shrinking only if it reads poorly.
- **Override storage layer.** Spec puts the `AccentColorOverride` on `CharacterProfileSaveData`. If you'd rather it lives on `CharacterCustomizationSaveData` (or wherever your existing cosmetic fields live), flag during implementation and reroute.
- **`Color32` vs `Color` NetworkVariable.** Spec picks `Color32` for blittability. If something downstream demands the 4-float `Color` (e.g. existing tinting code), use `Color` — replication still works, just at higher bandwidth.
- **Tail sprite asset.** A downward triangle sprite needs authoring. Acceptable placeholder during the implementation phase: a UGUI `Image` with no sprite assigned + a small `RectTransform` rotated 45° + clipped, or a 9-slice borrow from an existing triangle sprite. Final sprite is a visual-polish follow-up.
- **`???` localisation.** If/when localisation arrives, `???` should be a string-table key, not a string literal. Out of scope for this spec — current literal is acceptable.
- **Player's own accent colour selection UI.** Out of scope. The `SetAccentColor(Color)` setter ships dormant; the cosmetic UI to call it is a follow-up.

## 9. Done definition

Implementation is done when, with two players + multiple NPCs in a live session:

1. Every speech bubble visibly shows a coloured name strip on top with the speaker's name, in the speaker's archetype-default accent colour.
2. NPCs the local player has not "met by name" (`KnowsName == false`) render `???` in the name strip — accent colour still correct.
3. The bottom-most (newest) bubble in each stack has a downward tail; older bubbles do not. Pushing a new bubble moves the tail to the new bubble within one frame.
4. Distant speakers (>30u, X-Z plane) are invisible. Speakers at 12-30u are partially faded with smooth linear gradient. Within 12u, fully visible. The local player's own bubble never fades regardless of camera position.
5. The Habbo cross-character push still works — three nearby characters speaking each push the others' stacks up by the expected pixel height.
6. The late-joiner repro from §6 passes — fresh client joining mid-session sees correct names + accents + `???` per its local-player relationship state.
7. SKILL.md, the two wiki pages, and the agent descriptor are updated and the change log on `wiki/systems/character-speech.md` reflects the rework date.
8. `Assets/Prefabs/SpeechBubbleInstance_Prefab.prefab` no longer contains a `SeparatorLine` child. `SpeechBubbleInstance.cs` no longer contains `_separatorLine` / `SetSeparatorVisible` / `UpdateSeparatorVisibility`.
