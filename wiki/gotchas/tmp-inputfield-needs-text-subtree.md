---
type: gotcha
title: "TMP_InputField authored via reflection has no visible text"
tags: [ui, tmp, mcp-authoring, prefab]
created: 2026-05-13
updated: 2026-05-13
sources:
  - "[Commit c079a539](../../Assets/UI/Player%20HUD/UI_ShopBuyRow.prefab)"
related:
  - "[[player-ui]]"
status: mitigated
confidence: high
---

# TMP_InputField authored via reflection has no visible text

## Summary
`TMP_InputField` added through `AddComponent` (reflection / MCP `gameobject-component-add`) does **not** auto-create the nested `Text Area / Text` subtree that Unity's editor-menu "Add Component → TMP_InputField" does. The field stores the value but has no `TMP_Text` renderer bound — the input appears as an empty box even though `inputField.text` returns the correct string.

## Symptom
- Authoring an InputField via MCP tooling (`assets-prefab-create` + `gameobject-component-add ["TMPro.TMP_InputField"]`).
- At runtime: typing into the field or programmatically setting `_quantityInput.text = "0"` shows no character — the field looks blank.
- `inputField.text` returns the expected string; `inputField.textComponent` and `inputField.textViewport` are both `null`.

## Root cause
Unity's editor menu wraps `AddComponent<TMP_InputField>` with a custom inspector handler that also instantiates a `Text Area (RectMask2D)` child with a `Text (TextMeshProUGUI)` grandchild, then wires `m_TextViewport` and `m_TextComponent` to them. The reflection `AddComponent` path skips the handler — only the bare `TMP_InputField` component lands. With `m_TextComponent` null, the field has no renderer to surface its value.

## How to avoid
When authoring `TMP_InputField` prefabs via MCP / reflection / `gameobject-component-add`, **always also build the subtree manually**:

```
QuantityInput
├─ TMP_InputField (this component)
├─ Image (background, this component)
└─ Text Area
   ├─ RectMask2D
   └─ Text
      └─ TextMeshProUGUI (visible renderer)
```

Wire the InputField:
- `textViewport` → `Text Area`'s `RectTransform`
- `textComponent` → `Text`'s `TextMeshProUGUI`

Set anchors to stretch-fill in both `Text Area` and `Text` (with a small inset on Text Area for padding — typical `offsetMin (6,4) / offsetMax (-6,-4)`).

## How to fix (if already hit)
1. Add `Text Area` GameObject as child of the InputField root. Add `RectMask2D`.
2. Add `Text` GameObject as child of `Text Area`. Add `TextMeshProUGUI`.
3. Set both RectTransforms to anchor stretch-fill (Text Area with padding inset).
4. Wire the InputField via property assignment. Note: the Reflector path-patch API (`mcp__ai-game-developer__gameobject-component-modify` with `pathPatches` for `m_TextViewport`) **silently no-ops** for RectTransform references — use `script-execute` to assign the properties directly:

```csharp
inputField.textViewport = textArea;   // RectTransform
inputField.textComponent = text;      // TMP_Text
UnityEditor.EditorUtility.SetDirty(inputField);
```

5. Also set `contentType` and `characterValidation` to your desired validation rules (`IntegerNumber`, `Alphanumeric`, etc.) — the field is not picky about these but defaults to `Standard` which accepts anything.

## Affected systems
- [[player-ui]] — any HUD window prefab authored via MCP that includes a numeric stepper input.

## Links
- Commit `c079a539` — the UI_ShopBuyRow quantity stepper fix that surfaced this.
- Related: `Assets/UI/Player HUD/UI_ShopBuyRow.prefab`.

## Sources
- 2026-05-13 debugging session — empty quantity field on the shop buy panel stepper.
- Unity TMP source: `TMP_InputField.OnValidate` does check the subtree but does not auto-create it.
