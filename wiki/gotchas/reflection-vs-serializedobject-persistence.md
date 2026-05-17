---
type: gotcha
title: "Reflection SetValue on scene SerializeField doesn't persist — must use SerializedObject"
tags: [unity-editor, scene-authoring, serialization, mcp, roslyn, persistence]
created: 2026-05-17
updated: 2026-05-17
sources:
  - "Assets/Scripts/UI/PlayerUI.cs"
  - "Assets/Scenes/GameScene.unity"
  - "2026-05-17 conversation with Kevin — combat bar click no-op debug session"
related:
  - "[[player-hud]]"
status: mitigated
confidence: high
---

# Reflection SetValue on scene SerializeField doesn't persist — must use SerializedObject

## Summary
When authoring a scene via Roslyn / MCP / editor scripts, **don't use `FieldInfo.SetValue(component, value)` to wire a `[SerializeField]` reference**. Unity tracks scene-side serialization via the **SerializedObject / SerializedProperty** API. Reflection writes bypass that tracking — the in-memory value updates but Unity doesn't mark the property dirty, so `EditorSceneManager.SaveScene` writes back the **original** value from the loaded scene.

The result: at-Editor-time the field looks correct (the in-memory reference points where you wanted it), but the next time the scene is loaded (e.g. on entering Play mode), the field reverts to its previous serialized state — usually `null`.

## Why it bites
Reflection works for one specific case: **fresh components on fresh GameObjects** (e.g. `gameObject.AddComponent<Foo>()` immediately followed by `f.SetValue(...)`). Unity will serialize these from the in-memory state on first save because there's no prior serialized value to compare against. So a Roslyn script that creates a new prefab tree + uses reflection on its own new components looks like it works.

It then **silently fails** for the very next case: scene-already-has-the-component, edit one SerializeField via reflection. The reflection write wins in-memory, the scene file's existing serialized value wins on save. Reload → field is null.

This bit the **combat action bar** (2026-05-17) — a Roslyn script wired `PlayerUI._combatActionMenu` and `PlayerUI._combatItemsWindow` via reflection. Editor-time inspection showed the fields set. Play-mode opened → fields were `null` → `Initialize(character)` never ran on the bar → button click handlers never attached → clicking Melee Attack did nothing. ~30 minutes of root-cause debug.

## The canonical pattern

**Wrong (silently lost on save):**
```csharp
var f = playerUI.GetType().GetField("_combatActionMenu", BindingFlags.Instance | BindingFlags.NonPublic);
f.SetValue(playerUI, sceneBarScript);
EditorSceneManager.SaveScene(playerUI.gameObject.scene);
// Looks fine at runtime; reload → _combatActionMenu == null
```

**Right (persists to scene file):**
```csharp
var so = new SerializedObject(playerUI);
var p = so.FindProperty("_combatActionMenu");
p.objectReferenceValue = sceneBarScript;
so.ApplyModifiedPropertiesWithoutUndo();           // marks dirty, queues serialization

EditorSceneManager.MarkSceneDirty(playerUI.gameObject.scene);
EditorSceneManager.SaveScene(playerUI.gameObject.scene);
// Persists correctly across scene reload + Play mode
```

Use `ApplyModifiedPropertiesWithoutUndo()` (or `ApplyModifiedProperties()` if you want an Undo entry) — without one of those, the SerializedObject changes are stranded in the intermediate buffer.

## When reflection IS safe
- **Brand-new GameObject + brand-new component**: reflection works because Unity serializes the in-memory state on first save (nothing to compare against).
- **In-prefab authoring with `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset`**: prefab serialization always reads in-memory state of every loaded component. Reflection here is OK (but SerializedObject is still preferred for consistency).

## When reflection IS NOT safe
- **Scene component that existed before your script ran** — including any component on a GameObject loaded from the scene file.
- **Any field on a prefab instance in a scene** — the prefab instance has serialized overrides that compare to the prefab asset; reflection bypasses the override tracking.

Rule of thumb: if you're not 100% sure the component is fresh-this-frame, use SerializedObject.

## Detection
Symptom on next-session / Play-mode entry:
- A `SerializeField` you "set" via reflection reads as `null` (or its pre-script value).
- Null-guard warnings fire in the Console (e.g. the rule-#39 `PlayerUI.Open<Name>Window` null-guard).
- Behavior that depends on that field silently no-ops (no exception, no error).

Diagnostic script (run via `script-execute` after the suspected save):
```csharp
using System.Reflection;
var f = playerUI.GetType().GetField("_combatActionMenu", BindingFlags.Instance | BindingFlags.NonPublic);
Debug.Log($"Runtime value: {f.GetValue(playerUI)}");
var so = new SerializedObject(playerUI);
Debug.Log($"Serialized value: {so.FindProperty(\"_combatActionMenu\").objectReferenceValue}");
// If these disagree, you've hit this gotcha.
```

## Related rules
- **CLAUDE.md rule #39** — UI HUD prefab architecture: "Play-mode wiring is volatile. Wiring `PlayerUI._<name>Window` while in Play mode does not persist — Unity reverts on exit. Always wire in Edit mode (or via `SerializedObject.ApplyModifiedPropertiesWithoutUndo + EditorSceneManager.SaveScene` from an Editor script)." This gotcha is the deeper why behind that rule.

## Change log
- 2026-05-17 — Created. ~30 min debug session on combat-bar click no-op. Three Roslyn scripts had to be re-run with SerializedObject before PlayerUI._combatActionMenu + _combatItemsWindow stuck. — claude

## Sources
- `Assets/Scripts/UI/PlayerUI.cs` — the SerializeFields that hit this
- Scripts run via MCP `script-execute` during combat-bar prefab authoring + scene wiring
- 2026-05-17 conversation with [[kevin]] — "clicking on melee attack doesn't do anything"
