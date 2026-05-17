# Combat action bar — Manual prefab authoring checklist

**Sister doc to:** [2026-05-17-combat-action-bar.md](2026-05-17-combat-action-bar.md)

**Why this exists:** Tasks 7-12 of the main plan included prefab authoring steps. Unity MCP was unavailable during the execution session, so all `.cs` scripts landed but **zero prefabs were authored**. This doc lists every prefab + scene wiring change required to make the new combat action bar visible in the running game.

**Pre-reqs:**
- Unity Editor open with the project loaded.
- Unity MCP (`ai-game-developer__*` tools) connected — OR you're authoring by hand in the Editor.
- Familiarity with [rule #39 UI HUD prefab architecture](../../../CLAUDE.md#ui-hud-prefab-architecture) and [.agent/skills/ui-hud/SKILL.md](../../../.agent/skills/ui-hud/SKILL.md) (the canonical MCP recipe).

**Validation cadence:** after each prefab, do a quick Play-mode smoke test. Don't author all five and discover at the end that one is broken.

---

## File map

**Prefabs to create (5 new + 1 modify):**

| Asset | Kind | Location |
|---|---|---|
| `UI_CombatItemRow.prefab` | leaf (NOT a UI_WindowBase variant — no close button) | `Assets/UI/Player HUD/` |
| `UI_CombatItemsWindow.prefab` | **Variant of `UI_WindowBase.prefab`** (rule #39) | `Assets/UI/Player HUD/` |
| `UI_CombatAbilitySlot.prefab` | leaf | `Assets/UI/Player HUD/Combat/` |
| `UI_CombatInitiativeBar.prefab` | leaf | `Assets/UI/Player HUD/Combat/` |
| `UI_CombatQueuedLabel.prefab` | leaf | `Assets/UI/Player HUD/Combat/` |
| `UI_CombatActionMenu` (existing prefab) | modify — rebuild internal tree to 3-cluster layout | wherever the existing prefab lives |

**Scene wiring (one scene file):**
- The scene that hosts `PlayerUI` — assign the new `_combatItemsWindow` SerializeField + verify the existing `_combatActionMenu` SerializeField is still wired after the prefab rebuild.

---

## 1. UI_CombatItemRow.prefab (leaf)

**Backing script:** [UI_CombatItemRow.cs](../../../Assets/Scripts/UI/Combat/UI_CombatItemRow.cs) (commit `ff55993a`).

**Structure:**

```
UI_CombatItemRow (RectTransform, ~280 × 44, HorizontalLayoutGroup)
├── Icon (Image, 28×28)                        → wire to _icon
├── Meta (RectTransform, flex, VerticalLayoutGroup)
│   ├── Name (TMP_Text, font 11, bold)         → wire to _nameText
│   └── Effect (TMP_Text, font 9, color #9ab)  → wire to _effectText
├── Hotkey (TMP_Text, font 9, mono, in a small 14×14 dark frame)  → wire to _hotkeyText
├── Button (full-row Button overlay)           → wire to _rowButton
└── (Root) CanvasGroup                         → wire to _canvasGroup
```

**Background:** `rgba(42,42,53,1)` rounded rect (`Sprite` UI default or a 9-slice if you have one). 6 px inner padding. 24 px row spacing.

**Authoring (MCP):**
```
mcp__ai-game-developer__gameobject-create with name "UI_CombatItemRow"
mcp__ai-game-developer__gameobject-component-add with componentName "RectTransform"
mcp__ai-game-developer__gameobject-component-add with componentName "MWI.UI.Combat.UI_CombatItemRow"
mcp__ai-game-developer__gameobject-component-add with componentName "CanvasGroup"
mcp__ai-game-developer__gameobject-component-add with componentName "HorizontalLayoutGroup"
# build the child tree per the structure above
# use gameobject-component-modify (pathPatches) to wire the 7 SerializeField references
mcp__ai-game-developer__assets-prefab-create at "Assets/UI/Player HUD/UI_CombatItemRow.prefab"
mcp__ai-game-developer__gameobject-destroy (the scene instance)
```

**Validation:** drop the prefab into a fresh empty scene as a child of any Canvas. Add a temporary debug call to `Initialize(consumable, 1, _ => Debug.Log("clicked"))` from an editor script — confirm row renders the consumable's name + icon, click fires the callback.

---

## 2. UI_CombatItemsWindow.prefab (Variant of UI_WindowBase.prefab)

**Backing script:** [UI_CombatItemsWindow.cs](../../../Assets/Scripts/UI/Combat/UI_CombatItemsWindow.cs) (commit `8038cfef`).

**Rule #39 mandatory:** this is a `UI_WindowBase` variant. Author via Prefab Variant flow, NOT a fresh prefab.

**Structure (inside the inherited `Canvas` child):**

```
Canvas  (inherited from UI_WindowBase.prefab — ScreenSpaceCamera, sortingOrder=60)
├── Panel_Main_Background  (inherited — fixed size 280 × ~280 px; NO ContentSizeFitter here)
│   ├── Header  (RectTransform, 280 × 32, HorizontalLayoutGroup)
│   │   ├── Title (TMP_Text "Use Item", font 11, bold)
│   │   ├── HeaderCount (TMP_Text "X available", font 9, color #999)  → wire to _headerCountText
│   │   └── (inherited close button slot on the right — _buttonClose)
│   ├── ScrollView (RectTransform, 280 × ~240)
│   │   ├── Viewport
│   │   │   └── Content (VerticalLayoutGroup + ContentSizeFitter VERTICAL FIT ONLY)
│   │   │       (rows instantiated at runtime via UI_CombatItemRow.prefab)  → wire Content as _rowContainer
│   │   ├── Scrollbar (vertical, optional)
│   │   └── ScrollRect (on ScrollView)
│   └── Footer (TMP_Text "Click row to use · ESC close", font 9, color #888)
└── (inherited close button already authored on UI_WindowBase.prefab)
```

**Critical rule #39 reminders:**
- Canvas `renderMode` MUST be `ScreenSpaceCamera` (inherited — do NOT change).
- Canvas `sortingOrder` = 60 (above the action bar at 50).
- `worldCamera` left null at prefab time (UI_WindowBase.Awake assigns Camera.main at runtime).
- Canvas RectTransform scale MUST be `(1,1,1)` — override the inherited zero-scale if present.
- `ContentSizeFitter` ONLY on `ScrollView/Viewport/Content` (vertical fit) — NEVER on Panel_Main_Background or any fixed-size frame.

**Window dimensions:** 280 px wide, ~280 px tall (auto-grows with row count up to ScrollView Content's max).

**Anchoring:** right edge of screen, vertically offset to sit above the Items button in the action bar (~74 px). Anchor preset = bottom-right; offset Y ~+74.

**SerializeField wiring on the UI_CombatItemsWindow script:**
| Field | Target |
|---|---|
| `_rowContainer` | `ScrollView/Viewport/Content` RectTransform |
| `_rowPrefab` | `Assets/UI/Player HUD/UI_CombatItemRow.prefab` |
| `_headerCountText` | `Header/HeaderCount` TMP_Text |
| `_buttonClose` (inherited from UI_WindowBase) | inherited close button (already wired in base prefab) |

**Authoring (MCP):**
```
mcp__ai-game-developer__assets-find with searchFilter "t:Prefab UI_WindowBase" → resolve path
mcp__ai-game-developer__assets-prefab-instantiate with prefabRef <UI_WindowBase.prefab>
mcp__ai-game-developer__gameobject-modify (rename root) → "UI_CombatItemsWindow"
mcp__ai-game-developer__gameobject-component-add with componentName "MWI.UI.Combat.UI_CombatItemsWindow"
# Build header / scroll / footer tree inside the inherited Canvas child
# Verify Canvas sortingOrder = 60, scale = (1,1,1)
# Wire SerializeFields per the table above
mcp__ai-game-developer__assets-prefab-create at "Assets/UI/Player HUD/UI_CombatItemsWindow.prefab"
  → connectGameObjectToPrefab: true   (this creates the variant)
mcp__ai-game-developer__assets-get-data on the saved file
  → verify PrefabUtility.GetCorrespondingObjectFromSource resolves to UI_WindowBase.prefab
mcp__ai-game-developer__gameobject-destroy (the scene instance)
```

**Validation:**
1. Open the prefab — confirm "Variant of UI_WindowBase" badge in the Inspector header.
2. Read the prefab YAML — confirm one `Canvas` (not two) with `m_RenderMode: 0` (`ScreenSpaceCamera`).
3. Enter Play-mode in a scene where `PlayerUI._combatItemsWindow` is wired (see section 6) — open the window via `PlayerUI.Instance.OpenCombatItemsWindow(yourCharacter)` from a debug script. Confirm:
   - Window appears, close button works, ESC works.
   - Rows render for your inventory's consumables.
   - Click a row → `TryQueueUseItem` fires (look for the queued label appearing on the action bar, OR add temporary `Debug.Log` to `OnRowUsed`).
   - Auto-close on combat end works (force `CharacterCombat.OnBattleLeft` invocation).

---

## 3. UI_CombatAbilitySlot.prefab (leaf, ×6 instances)

**Backing script:** [UI_CombatAbilitySlot.cs](../../../Assets/Scripts/UI/Combat/UI_CombatAbilitySlot.cs) (commit `6a76ac36`).

**Structure:**

```
UI_CombatAbilitySlot (RectTransform, 26×26, with 1 px dark border via Image)
├── Icon (Image, 24×24, centered)              → wire to _icon
├── CannotUseOverlay (Image, 24×24, rgba(0,0,0,0.6), initially disabled)  → wire to _cannotUseOverlay
├── HotkeyText (TMP_Text, font 7, mono, bottom-right corner)  → wire to _hotkeyText
├── EmptyPlaceholder (Image with hatched/striped sprite)  → wire to _emptyPlaceholder
└── Button (full-cover Button overlay)         → wire to _clickButton
```

**Background:** `rgba(42,42,53,1)`. Border `rgba(255,255,255,0.1)`. 4 px corner radius if using a 9-slice.

**Empty placeholder hatched sprite:** repeating-diagonal-stripes 45deg; if you don't have one, use a flat `rgba(35,35,46,1)` Image as a fallback (matches the mockup's empty slot look).

**SerializeField wiring on the UI_CombatAbilitySlot script:**
| Field | Target |
|---|---|
| `_icon` | `Icon` Image |
| `_cannotUseOverlay` | `CannotUseOverlay` Image (initially `enabled=false`) |
| `_hotkeyText` | `HotkeyText` TMP_Text |
| `_emptyPlaceholder` | `EmptyPlaceholder` GameObject (initially `SetActive=false` if a slot is wired, or true if empty) |
| `_clickButton` | `Button` |

**Authoring (MCP):** same recipe as section 1 (gameobject-create → add components → wire SerializeFields → assets-prefab-create). Save to `Assets/UI/Player HUD/Combat/UI_CombatAbilitySlot.prefab`.

**Validation:** instantiate 6 copies in a test scene as children of a HorizontalLayoutGroup. Call `Initialize(0, character)` through `Initialize(5, character)` on each. Confirm icons, hotkey labels (1-6), and empty placeholder behavior.

---

## 4. UI_CombatInitiativeBar.prefab (leaf)

**Backing script:** [UI_CombatInitiativeBar.cs](../../../Assets/Scripts/UI/Combat/UI_CombatInitiativeBar.cs) (commit `8cc6b478`).

**Structure:**

```
UI_CombatInitiativeBar (RectTransform, 200×6, with background)
├── Background (Image, rgba(0,0,0,0.7), 1 px border rgba(255,255,255,0.2))
└── Fill (Image, Type=Filled, FillMethod=Horizontal, FillAmount=0)  → wire to _fill
    └── orange→yellow gradient sprite (or solid #ffaa00 with manual gradient later)
```

**SerializeField wiring on the UI_CombatInitiativeBar script:**
| Field | Target |
|---|---|
| `_fill` | `Fill` Image (Image.type = `Filled`, fillMethod = `Horizontal`, fillOrigin = `Left`) |

**Authoring:** standard leaf prefab. Save to `Assets/UI/Player HUD/Combat/UI_CombatInitiativeBar.prefab`.

**Validation:** instantiate in a test scene, call `Initialize(character)`. Confirm the fill grows as `CharacterCombat.OnInitiativeChanged` fires (you can manually invoke from a debug script).

---

## 5. UI_CombatQueuedLabel.prefab (leaf)

**Backing script:** [UI_CombatQueuedLabel.cs](../../../Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs) (commit `f1fd48c8`).

**Structure:**

```
UI_CombatQueuedLabel (RectTransform, auto-size with HorizontalLayoutGroup + ContentSizeFitter horizontal)
└── VisualRoot (GameObject, initially SetActive=false)  → wire to _visualRoot
    ├── Background (Image, rgba(26,58,107,0.95), 1 px border #3a78c8, rounded 12 px, optional Outline)
    └── Label (TMP_Text "▶ Queued: ...", font 10, color #cce, 4 px inner padding)  → wire to _label
```

**Optional polish:** add a `Shadow` or `Outline` component for the glow effect (mockup showed `box-shadow: 0 0 12px rgba(58,120,200,0.5)` — closest Unity equivalent is `Outline` with color matching).

**SerializeField wiring on the UI_CombatQueuedLabel script:**
| Field | Target |
|---|---|
| `_label` | `Label` TMP_Text |
| `_visualRoot` | `VisualRoot` GameObject |

**Authoring:** standard leaf prefab. Save to `Assets/UI/Player HUD/Combat/UI_CombatQueuedLabel.prefab`.

**Validation:** instantiate, call `Initialize(character)`. Trigger `CharacterCombat.OnActionIntentDecided` manually — confirm label shows `▶ Queued: Action → <target>`. Then trigger `OnActionIntentCleared` — confirm it hides.

---

## 6. UI_CombatActionMenu — modify existing prefab

**Backing script:** [UI_CombatActionMenu.cs](../../../Assets/Scripts/UI/UI_CombatActionMenu.cs) (commit `2cc8b34e` — full rewrite).

**Existing prefab location:** find via `mcp__ai-game-developer__assets-find with searchFilter "t:Prefab UI_CombatActionMenu"` (probably under `Assets/UI/Player HUD/` or `Assets/Prefabs/UI/`).

**Old structure (pre-rewrite):** single Menu container with one Attack button + label.

**New structure (post-rewrite):**

```
UI_CombatActionMenu (RectTransform — keep existing root + Canvas if separate)
└── _menuContainer (GameObject — shown/hidden by IsInBattle)
    ├── Chrome (anchored above the bar row)
    │   ├── UI_CombatQueuedLabel.prefab (instance)    → wire to _queuedLabel
    │   └── UI_CombatInitiativeBar.prefab (instance)  → wire to _initiativeBar
    ├── Bar (HorizontalLayoutGroup, dark rounded background)
    │   ├── Cluster_Weapon (HorizontalLayoutGroup)
    │   │   ├── Btn_Attack (Button)                   → wire to _attackButton
    │   │   │   ├── AttackText (TMP_Text)             → wire to _attackButtonText
    │   │   │   └── AmmoBadge (GameObject)            → wire to _ammoBadgeRoot
    │   │   │       └── AmmoText (TMP_Text)           → wire to _ammoBadgeText
    │   │   └── ReloadRoot (GameObject)               → wire to _reloadButtonRoot
    │   │       └── Btn_Reload (Button)               → wire to _reloadButton
    │   ├── Sep_1 (1px vertical line)
    │   ├── Cluster_Abilities (HorizontalLayoutGroup)
    │   │   └── 6 × UI_CombatAbilitySlot.prefab instances  → wire to _abilitySlots[0..5]
    │   ├── Sep_2 (1px vertical line)
    │   └── Cluster_Utility (HorizontalLayoutGroup)
    │       ├── Btn_Swap (Button)                     → wire to _swapButton
    │       │   ├── SwapFrom (TMP_Text)               → wire to _swapFromText
    │       │   ├── SwapArrow (TMP_Text "⇄")
    │       │   ├── SwapTo (TMP_Text)                 → wire to _swapToText
    │       │   └── SwapCanvasGroup (CanvasGroup)     → wire to _swapCanvasGroup
    │       └── Btn_Items (Button)                    → wire to _itemsButton
```

**SerializeField wiring on the rewritten UI_CombatActionMenu script:**
| Field | Target |
|---|---|
| `_menuContainer` | the GameObject toggled show/hide by IsInBattle |
| `_attackButton` / `_attackButtonText` | Btn_Attack + its TMP_Text label |
| `_ammoBadgeRoot` / `_ammoBadgeText` | AmmoBadge + its TMP_Text |
| `_reloadButtonRoot` / `_reloadButton` | ReloadRoot + Btn_Reload |
| `_abilitySlots` (length 6) | 6 instances of UI_CombatAbilitySlot.prefab, ordered 0→5 |
| `_swapButton` / `_swapFromText` / `_swapToText` / `_swapCanvasGroup` | Btn_Swap + its three TMP_Texts + a CanvasGroup |
| `_itemsButton` | Btn_Items |
| `_initiativeBar` | the UI_CombatInitiativeBar.prefab instance child |
| `_queuedLabel` | the UI_CombatQueuedLabel.prefab instance child |

**Authoring approach (recommended sequence):**
1. Open the existing prefab.
2. Take a screenshot of the current hierarchy for rollback reference.
3. Delete the old single-button structure (leave the root + `_menuContainer` if you want to keep their settings).
4. Build the new structure inside `_menuContainer` per the tree above. Drag in the four leaf prefabs (UI_CombatInitiativeBar, UI_CombatQueuedLabel, ×6 UI_CombatAbilitySlot).
5. Wire every SerializeField (11 fields + 6 array elements).
6. Save the prefab.
7. The scene instance (under `UI_PlayerHUD`) should propagate the new structure automatically. Verify there's no broken override.

**Visual styling (placeholder OK for v1):**
- Bar background: `rgba(15,15,20,0.85)`, 1 px border `rgba(255,255,255,0.12)`, 6 px corner radius, 6/8 px padding.
- Cluster separators: 1×22 px `rgba(255,255,255,0.1)` lines.
- Button backgrounds: `rgba(42,42,53,1)`. Primary (Attack): `#3b4a6b` border `#5a78a8`. Queued: `#1a3a6b` border `#3a78c8` with glow Outline.
- Hotkey labels: bottom-right corner of each button, 7 px font, `#888`.

Visual polish per rule #39 is a separate authoring pass — placeholder appearance is acceptable.

**Validation:** Play-mode smoke test in any scene where a Character can enter combat:
1. Walk into a battle → bar appears with the 3 clusters visible.
2. Equip a sword → Reload + AmmoBadge hidden, Attack reads "Melee Attack".
3. Equip a bow → Attack reads "Ranged Attack", no Reload.
4. Equip a pistol → Attack reads "Ranged Attack [3/6]", Reload button appears.
5. Fire 3 shots → ammo decrements via the new NetworkVariable.
6. Empty mag → Attack greyed, click anyway → Reload auto-queues.
7. Reload completes (2s) → ammo restores to MagazineSize.
8. Click Swap with 2 weapons → 0.5s swap, cluster re-renders.
9. Click Items → window opens; click a row → window closes, queued label appears above bar.
10. Press 1-6 with non-empty ability slots → those abilities trigger.
11. Press R / Y / Space hotkeys → same effects as the buttons.
12. Press E in battle → toggles items window (preempts the field-eat E dispatcher).
13. Exit battle → bar hides, items window auto-closes if open.

---

## 7. Scene wiring (`PlayerUI._combatItemsWindow`)

The scene that hosts `PlayerUI` needs the new `_combatItemsWindow` SerializeField assigned.

**Authoring (MCP):**
```
mcp__ai-game-developer__scene-list-opened → find the play scene
mcp__ai-game-developer__gameobject-find with name "UI_PlayerHUD" (or wherever the PlayerUI script lives)
mcp__ai-game-developer__assets-prefab-instantiate with prefabRef "Assets/UI/Player HUD/UI_CombatItemsWindow.prefab"
  → parent under UI_PlayerHUD (sibling of UI_StorageFurniturePanel, UI_SafePanel, etc.)
mcp__ai-game-developer__gameobject-modify on the new instance:
  - SetActive(false)
  - RectTransform anchor: bottom-right; anchored position offset to sit above Items button (~y=74)
mcp__ai-game-developer__gameobject-component-modify on PlayerUI:
  - set _combatItemsWindow → reference the new scene instance
mcp__ai-game-developer__scene-save
```

**Per rule #39:** wiring must be done in Edit mode (not Play mode — Unity reverts on exit). Use `SerializedObject.ApplyModifiedPropertiesWithoutUndo` + `EditorSceneManager.SaveScene` from an Editor script if doing this via MCP code execution.

**Verify after save:**
- `PlayerUI._combatItemsWindow` is not null when entering Play mode.
- Opening the window via `PlayerUI.Instance.OpenCombatItemsWindow(character)` does not log the `[PlayerUI] OpenCombatItemsWindow called but _combatItemsWindow SerializeField is null` warning.

**Verify existing wiring after the action bar prefab rebuild:**
- `PlayerUI._combatActionMenu` still references the (rebuilt) UI_CombatActionMenu scene instance.
- All other SerializeFields on PlayerUI (_safePanel, _storagePanel, etc.) still wired.

---

## 8. Post-authoring full validation

After every prefab is authored and scene wiring is complete:

1. **Compile check.** Unity Console → zero errors, zero warnings (other than pre-existing).
2. **Late-joiner repro** (rule #19b). Host the session, fire 3 pistol rounds (ammo 3/6 visible on host bar). Have a fresh client join and look at the host's character — verify their CharacterEquipment's `_activeAmmoNet` syncs (this is a remote-character ammo check — the bar UI is owner-only per Option A, but the NetworkVariable replication is testable via the Dev Mode inspector if available).
3. **Multiplayer hotkeys.** Each player presses Space / R / Y / 1-6 / E. Confirm their own actions fire; no cross-player interference.
4. **Combat-end auto-close.** Open items window mid-combat, end combat, confirm window auto-closes via `OnBattleLeft`.
5. **E-dispatcher preempt.** Hold a Health Potion in-hand, enter combat, press E → items window opens (NOT the field-eat path). Exit combat, press E again with potion in hand → field-eat fires (existing priority 5).
6. **Out-of-battle Space preserved.** Out of combat, press Space → existing direct-Attack(null) behavior unchanged.

If any validation step fails, fix before moving on (don't ship a half-broken prefab).

---

## 9. After validation passes

- Optionally take a screenshot of the working bar in-game and attach to the main plan's PR description.
- Mark Task 7-12's "prefab work" items complete in the main plan.
- Proceed to Task 15 (wiki + SKILL + agent docs) and Task 14 (full multiplayer playtest matrix).

---

## What to do if you get stuck

- **"My prefab variant is showing the inherited Canvas at scale (0,0,0)"** → see `wiki/systems/player-hud.md` "scale=(0,0,0) invisibility" gotcha. Force scale to `(1,1,1)` on the variant via override.
- **"My window is visible in Scene view but invisible in Game view"** → second Canvas on the root from an `Awake` `AddComponent` call. Check `panelRoot.GetComponentsInChildren<Canvas>(true)`. Delete any extras (rule #39 + 2026-05-16 SafeFurniture incident).
- **"My Panel_Main_Background collapses to (0,0)"** → there's a `ContentSizeFitter` on it. Remove. ContentSizeFitter belongs ONLY on ScrollView/Viewport/Content.
- **"My SerializeField references go null after exiting Play mode"** → you wired in Play mode. Re-wire in Edit mode + save the scene.
- **"My ability slot stays empty even though I have an ability equipped"** → `CharacterAbilities.GetActiveSlot(slotIndex)` returns null. Check the character actually has an ability in slot `slotIndex` via Dev Mode Inspect. Also confirm `UI_CombatActionMenu.Initialize` ran (it calls Initialize on every slot).
- **"My queued label stays empty"** → the v1 label uses a generic "Action" placeholder because `PlannedAction` has no semantic identity. That's expected for v1 — see commit `f1fd48c8` description. Future polish adds an ActionDescriptor.
