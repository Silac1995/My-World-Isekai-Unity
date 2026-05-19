---
type: gotcha
title: "Newtonsoft.Json explodes on UnityEngine.Color via the linear/gamma property loop"
tags: [persistence, save-load, newtonsoft, json, color, gotcha]
created: 2026-05-20
updated: 2026-05-20
sources:
  - "Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs"
  - "Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs"
  - "Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs"
  - "2026-05-20 conversation with Kevin — speech-bubble rework Task 22 fallout"
related:
  - "[[character-persistence]]"
  - "[[character-speech]]"
status: mitigated
confidence: high
---

# Newtonsoft.Json explodes on UnityEngine.Color via the linear/gamma property loop

## Summary
Adding a `UnityEngine.Color` field directly to any class serialized by `SaveFileHandler.WriteProfileAsync` (Newtonsoft.Json) breaks the save with a `JsonSerializationException: Self referencing loop detected for property 'linear' with type 'UnityEngine.Color'`. The fix is to use `Color32` (plain byte struct, no cyclic properties) at the save-data boundary and cast to/from `Color` in the runtime accessors.

## Symptom
- Saving a character profile (new-character create, bed checkpoint, portal-gate return, …) raises
  ```
  JsonSerializationException: Self referencing loop detected for property 'linear' with type 'UnityEngine.Color'. Path '<field name>'.
    at Newtonsoft.Json.Serialization.JsonSerializerInternalWriter.CheckForCircularReference(...)
    at SaveFileHandler.WriteProfileAsync(...)
  ```
- User-visible toast: "Failed to save character profile!"
- The Editor may freeze when the save handler retry-spams the exception (each retry walks the same property graph and re-throws).

## Root cause
`UnityEngine.Color` exposes two public properties — `linear` and `gamma` — that each return *a new* `Color`. Each returned Color has its own `.linear` and `.gamma` properties. There is no termination — it's an infinite property chain. Newtonsoft.Json's default `JsonSerializerSettings` walks every public property + field on a value, with `ReferenceLoopHandling.Error`, so it follows `linear → linear → linear → …` and throws on the first repeat.

`JsonUtility` (Unity's built-in) only walks `[SerializeField]`-marked fields, so it would NOT trip the loop on a Color. But the project's save path uses Newtonsoft, which walks properties by default.

The `linear`/`gamma` chain is benign at runtime — the properties are computed on-demand, no actual cycle in memory. It's purely a serializer-walks-property-graph issue.

This was masked during Task 6 of the 2026-05-19 speech-bubble rework because the only character whose save actually carried a `Color` was the player (single override path), and the test path went through `CharacterDataCoordinator` directly without exercising the production save. Task 22 (2026-05-20) made *every* character set `_hasAccentOverride = true` on spawn so the random accent persisted, and every subsequent save tripped the loop.

## How to avoid
Use **`Color32`** for any colour field that lives inside a class serialized by Newtonsoft.Json:

```csharp
// ❌ Newtonsoft walks Color.linear → Color → Color.linear → … and explodes.
public Color accentColorOverride;

// ✅ Color32 is a plain byte struct with no computed properties.
// Casts to/from Color implicitly so the surrounding code rarely needs to care.
public Color32 accentColorOverride;
```

At the save boundary:
```csharp
// Export:
data.accentColorOverride = (Color32)_character.AccentColorOverrideValue;

// Import:
_character.SetAccentColor((Color)data.accentColorOverride);
```

The byte-precision is identical to the runtime `NetworkVariable<Color32>` used to replicate the same value, so there's no truth-loss versus the wire format.

Alternative fixes that work but are heavier:
1. **Custom `JsonConverter<Color>`** registered on the `JsonSerializerSettings` `SaveFileHandler` uses. Writes/reads 4 floats. Verbose, repeats the standard "convert Color via reflection" recipe, and every consumer must opt into the same settings.
2. **`ReferenceLoopHandling.Ignore`** on the serializer settings. Silently drops cycles. Works, but masks other cycles you'd actually want to fail loudly. Avoid as a band-aid.
3. **Three primitive floats** (`accentR`, `accentG`, `accentB`). Works, but more verbose than `Color32` and loses the implicit cast that keeps the consumer code clean.

`Color32` is the recommended shape for this project.

## How to fix (if already hit)
1. Identify every `UnityEngine.Color` field on a save-data class (anything ever passed to `JsonConvert.SerializeObject` / `SaveFileHandler.WriteProfileAsync`). Grep: `Color\s+\w+;` inside `Assets/Scripts/Core/SaveLoad/` and `Assets/Scripts/Character/SaveLoad/`.
2. Swap the field type to `Color32`.
3. Cast `(Color32)source` on export and `(Color)data.field` on import. Both casts compile via Unity's implicit operators; the explicit cast is for documentation.
4. Recompile + verify the save flow.

Save-data schema migration is **free** for this specific change — Newtonsoft serialises a `Color32` as `{"r":120,"g":140,"b":190,"a":255}` (bytes) instead of a `Color` `{"r":0.47,"g":0.55,"b":0.75,"a":1.0,"linear":{...},...}`. Old save files written with `Color` cannot be re-loaded after the schema change (they never finished writing — the exception aborted the write), so there is no orphan data to migrate.

## Affected systems
- [[character-persistence]] — `CharacterProfileSaveData` was the first save-data class to carry a Color. Now uses `Color32`.
- [[character-speech]] — consumes the saved value via `Character.AccentColor` + `SetAccentColor` round-trip.

Future colour fields on save-data classes (e.g. UI customisation, eye / hair colour overrides) must follow the same `Color32` convention.

## Related rules
- **CLAUDE.md rule #38 — Editor vs build serialization tolerance.** Same shape of bug at a different layer: a serialiser that *handles* a value in one context (Unity's built-in JsonUtility) fails in another context (Newtonsoft). Always test the actual save path, not a synthetic round-trip.
- **CLAUDE.md rule #31 — Defensive coding.** `SaveFileHandler.WriteProfileAsync` does have a try/catch wrapper (which is why the user got a toast instead of a hard crash), but the retry loop spammed the same exception many times before the user noticed.

## Links
- [[character-persistence]]
- [[character-speech]]

## Sources
- [SaveFileHandler.cs](../../Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs) — line ~52, `JsonConvert.SerializeObject` callsite.
- [CharacterProfileSaveData.cs](../../Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs) — `accentColorOverride` field (now `Color32`).
- [CharacterDataCoordinator.cs](../../Assets/Scripts/Character/SaveLoad/CharacterDataCoordinator.cs) — export/import casts at the boundary.
- 2026-05-20 conversation with [[kevin]] — first hit while testing the speech-bubble rework's random-accent change (Task 22, commit `50399e50`). Fix landed in commit `49fcbaf0`.
