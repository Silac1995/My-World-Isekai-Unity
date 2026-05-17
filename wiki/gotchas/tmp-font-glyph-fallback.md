---
type: gotcha
title: "TMP font glyph fallback — LiberationSans missing Unicode renders as □"
tags: [ui, tmp, fonts, unicode, rendering]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "Assets/UI/Player HUD/UI_CombatItemsWindow.prefab"
  - "Assets/Scripts/UI/UI_CombatActionMenu.cs"
  - "Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs"
  - "2026-05-17 conversation with Kevin — combat bar TMP glyph squares"
related:
  - "[[player-hud]]"
  - "[[combat]]"
status: mitigated
confidence: high
---

# TMP font glyph fallback — LiberationSans missing Unicode renders as □

## Summary
The project ships **LiberationSans SDF** as the default TMP font. It covers Latin + common European Unicode but **does NOT include**:

- `🧪` (U+1F9EA, "Test Tube") — emoji range
- `⇄` (U+21C4, "Rightwards Arrow Over Leftwards Arrow") — math operators
- `▶` (U+25B6, "Black Right-Pointing Triangle") — geometric shapes
- Any emoji in general (U+1F000+)

When TMP encounters one of these in a string, it logs a warning and replaces the glyph with `□` (U+25A1, "White Square") at render time. The user sees a square in the UI. The warning is in the console but rarely surfaces during normal play.

Confirmed working in LiberationSans: `→` (U+2192), `←` (U+2190), `↻` (U+21BB), `•` (U+2022), `»` `«` (U+00BB / U+00AB), `→` and most BMP plane glyphs below U+2000.

## Why it bites
Developer authoring a UI string (especially in code, where copy-pasted designs from chat / mockups often contain emoji or geometric shapes) sees the glyph render fine in their editor or in the source file — but at runtime TMP can't find it and substitutes the square. Easy to miss in a quick Play-mode glance because the substitution looks like an intentional bullet/marker.

This bit the **combat action bar** authoring (2026-05-17) three times in one session:
- `🧪 E` for the Items button → rendered `□ E`
- `⇄` for the Swap arrow → rendered `□`
- `▶ Queued:` for the queued-action label → rendered `□ Queued:`

## Fix patterns

**Cheapest (recommended):** use ASCII or BMP glyphs that LiberationSans has. Examples that have rendered cleanly across the project:
- "Items" instead of "🧪 Items"
- "/" or "→" instead of "⇄"
- "Queued:" (no leading glyph) or "» Queued:" instead of "▶ Queued:"

**Heavier:** add a fallback font with emoji coverage to TMP's fallback chain. See `Project Settings → TextMeshPro → Settings → Fallback Font Assets`. Costs runtime atlas building + memory. Only justified when emoji content is structurally required (player-typed chat, item names containing brand glyphs, etc.).

**Diagnostic:** if a UI shows squares where text should be, grep the source for any code-point above U+2200 or any emoji surrogate. If you authored the string in a chat or mockup tool, suspect Unicode it copied in.

## Detection
Console produces (filterable):
```
The character with Unicode value \U0001F9EA was not found in the [LiberationSans SDF]
font asset or any potential fallbacks. It was replaced by Unicode character □
in text object [Text].
```

Hot-path-safe: the warning fires once per missing-glyph per text object, not per frame.

## Change log
- 2026-05-17 — Created. Three combat-bar TMP glyphs (🧪, ⇄, ▶) hit this; all three replaced with ASCII / BMP-safe alternatives. — claude

## Sources
- `Assets/UI/Player HUD/UI_CombatItemsWindow.prefab` — Items button label "Items"
- `Assets/Scripts/UI/UI_CombatActionMenu.cs` (`WeaponIconGlyph`) — "Mle" / "Rng" instead of weapon emoji
- `Assets/Scripts/UI/Combat/UI_CombatQueuedLabel.cs` — dropped leading ▶ prefix
- 2026-05-17 conversation with [[kevin]] — square showing in queued label
