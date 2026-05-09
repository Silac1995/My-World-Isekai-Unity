---
type: gotcha
title: "Runtime UI children collapse under prefab VerticalLayoutGroup"
tags: [ui, layout, prefab, devmode, gotcha]
created: 2026-05-09
updated: 2026-05-09
sources:
  - "Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs"
  - "Assets/Resources/UI/DevModePanel.prefab"
  - "2026-05-09 conversation with Kevin — Console Management sub-tab debug session"
related:
  - "[[dev-mode]]"
status: mitigated
confidence: high
---

# Runtime UI children collapse under prefab VerticalLayoutGroup

## Summary
When you instantiate new sub-tab GameObjects at runtime inside a prefab GO that already has a `VerticalLayoutGroup`, two compounding traps can flatten your new children to ~30 px tall — visually empty even though they exist. (1) Legacy prefab wrapper children left in place still consume space via their `LayoutElement.minHeight` / `flex`. (2) The parent VLG's flex distribution silently misbehaves when siblings have mixed `flex=0` / `flex=1` LayoutElements alongside non-VLG children with TMP-derived preferred heights. The widgets render, the hierarchy looks correct in a runtime probe, the script is wired up — but the active sub-tab is clipped by its inner `RectMask2D` to a sliver showing only its first row.

## Symptom
- Programmatic UI build runs, no errors, no exceptions in console.
- Runtime probe via `script-execute` shows the new child GameObjects exist with non-zero rect sizes (552 x 22, 552 x 26, etc.) — they're real, sized, and active.
- The visible game view shows only the topmost row (e.g. a banner) and empty space below — the rest is clipped.
- Probing the parent rect: the **outer host** is 568 x ~30, even though the **inner ScrollRect content** is 568 x 644. The inner content size is right; the outer is wrong.
- Force-rebuilding the layout (`LayoutRebuilder.ForceRebuildLayoutImmediate`) doesn't help.
- Setting the outer VLG's `childForceExpandHeight = true` doesn't help either.

## Root cause
Two failures, and the visual result is the union of both:

**1. Legacy prefab children compete for VLG space.**
The DevModePanel prefab's `BuildingInspectorView` originally had `Header` + `Slots` (a ScrollRect wrapper carrying the serialized `_content` TMP_Text inside it). My runtime refactor moved the `_content` reference out of `Slots` into a new `OverviewContent` host, but **left the `Slots` GO itself in place** — it now contained only an empty Viewport + Scrollbar but kept its `LayoutElement(min=200, flex=10)`. The parent VLG dutifully gave `Slots` its 200+ pixels (and its flex=10 share of leftover), starving the runtime-built `OverviewContent` / `ConsoleManagementContent` siblings.

**2. VLG flex distribution misbehaves under mixed children.**
Even after hiding `Slots`, the active siblings ended up with `Header` (no LE — TMP-derived preferred height of ~62), `SubTabBar` (LE `min=28, pref=28, flex=0`), and the two content hosts (LE `flex=1`). With `childForceExpandHeight = true`, VLG should have given the leftover ~620 px to the active content host. In practice the host stayed at 34 px while Header inflated to 62 px. The exact distribution math depends on TMP's lazy layout pass + the editor's first-frame timing; the symptom reproduces deterministically once Awake runs while the panel is `SetActive(true)` for the first time.

## How to avoid
When you build new sub-tab UI inside an existing prefab GameObject that has its own `VerticalLayoutGroup`:

1. **Hide every legacy prefab child you don't explicitly want.** Don't `Destroy` — under `DontDestroyOnLoad` + Editor hot-reload, async Destroy timing is unreliable. Use `SetActive(false)`. Detach any serialized field references (e.g. a TMP_Text marked in the inspector) up to the root **before** hiding their wrapper, so they survive.

   ```csharp
   // Pull serialized refs to the root first.
   if (_content.transform.parent != transform)
       _content.transform.SetParent(transform, worldPositionStays: false);

   // Then hide every other child.
   for (int i = transform.childCount - 1; i >= 0; i--)
   {
       var c = transform.GetChild(i);
       if (c == _headerLabel?.transform) continue;
       if (c == _content?.transform) continue;
       c.gameObject.SetActive(false);
   }
   ```

2. **Bypass the parent VLG entirely for your new hosts.** Don't fight VLG flex distribution. Set `LayoutElement.ignoreLayout = true` and use manual stretch anchors. The math is trivial: top inset = sum of fixed widgets above + spacing, bottom inset = 0.

   ```csharp
   const float HeaderHeight = 36f;
   const float TabBarHeight = 28f;
   const float TopInset = HeaderHeight + TabBarHeight + 4f;

   // SubTabBar: top-stretch, anchored just below the header.
   tabBarRT.anchorMin = new Vector2(0, 1); tabBarRT.anchorMax = new Vector2(1, 1);
   tabBarRT.pivot = new Vector2(0.5f, 1f);
   tabBarRT.anchoredPosition = new Vector2(0, -HeaderHeight);
   tabBarRT.sizeDelta = new Vector2(0, TabBarHeight);
   tabBarGO.AddComponent<LayoutElement>().ignoreLayout = true;

   // Content host: full-stretch with top inset.
   hostRT.anchorMin = new Vector2(0, 0); hostRT.anchorMax = new Vector2(1, 1);
   hostRT.pivot = new Vector2(0.5f, 0.5f);
   hostRT.offsetMin = new Vector2(0, 0);
   hostRT.offsetMax = new Vector2(0, -TopInset);
   hostGO.AddComponent<LayoutElement>().ignoreLayout = true;
   ```

   See `BuildingInspectorView.ConfigureContentHostStretch` for the canonical helper.

## How to fix (if already hit)
Diagnose first — programmatic UI bugs masquerade as a dozen other issues:

1. **Probe the outer host rect, not the inner content.** Often the inner ScrollRect content fitter has computed a sane height (e.g. 644) but the outer host is clipped to 30. Read `hostRect.rect.height` on the outer GO.
2. **Sum the active children's rect heights.** If the total is much less than the parent's rect, the parent VLG is misbehaving (mixed flex symptom). If a single inactive-looking child has a giant rect (e.g. 542), it's a legacy wrapper still taking space.
3. **Apply the two fixes above** in order: hide legacy children first, then switch the runtime hosts to `ignoreLayout` + manual anchors. Don't try to tune `childForceExpandHeight` — under mixed children it doesn't reliably solve the problem.

## Affected systems
- [[dev-mode]] — `BuildingInspectorView` Console Management sub-tab. The pattern generalizes to any future runtime sub-tab construction inside a prefab-authored VLG host.

## Links
- [[dev-mode]]
- `Assets/Editor/DevMode/DevInspectTabBuilder.cs` — the alternate strategy: author the hierarchy via an Editor menu utility, save as a prefab, no runtime VLG fight. Preferred when the sub-tab structure is static (e.g. CharacterInspectorView's 10 sub-tabs). Runtime construction is only worth it for very small (≤2) sub-tab counts.

## Sources
- [BuildingInspectorView.cs](../../Assets/Scripts/Debug/DevMode/Inspect/BuildingInspectorView.cs) — `BuildSubTabHierarchy` + `ConfigureContentHostStretch` carry the canonical fix.
- [DevModePanel.prefab](../../Assets/Resources/UI/DevModePanel.prefab) — the prefab that originally had the `Slots` wrapper.
- 2026-05-09 conversation with [[kevin]] — Console Management sub-tab debug session (commit `ef136b57`).
