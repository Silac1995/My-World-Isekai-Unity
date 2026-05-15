---
type: gotcha
title: "Unity-serialized self-referencing type crashes standalone build on load"
tags: [save-load, serialization, unity-serializer, build-vs-editor, mono, crash]
created: 2026-05-15
updated: 2026-05-15
sources:
  - "[CharacterProfileSaveData.cs](../../Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs) — `partyMembers` field"
  - "[MapSaveData.cs](../../Assets/Scripts/World/MapSystem/MapSaveData.cs) — `HibernatedNPCData.ProfileData`"
  - "[WildernessZone.cs](../../Assets/Scripts/World/Zones/WildernessZone.cs) — `_wildlife : List<HibernatedNPCData>`"
  - "[SaveFileHandler.cs](../../Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs) — default `JsonConvert.SerializeObject` / `DeserializeObject`"
  - "2026-05-15 player crash report from Kevin — build crashes during loading after clicking a character in CharacterSelectPanel; editor was fine"
related:
  - "[[save-load]]"
  - "[[party]]"
  - "[[world]]"
  - "[[character]]"
status: mitigated
confidence: high
---

# Unity-serialized self-referencing type crashes standalone build on load

## Summary
A `[System.Serializable]` C# class with a field of its own type (directly, or via `List<>` / array) creates a type-level recursion that Unity's reflection-based serializer walks **eagerly** whenever the type is reachable through a `[SerializeField]` / `[Serializable]` chain (e.g. via a `MonoBehaviour` field). The walk stops at Unity's hardcoded depth-10 limit. In the **editor**, you just see a yellow `"Serialization depth limit 10 exceeded at 'X.Y'. There may be an object composition cycle…"` warning and the game keeps running. In a **standalone Mono build on Windows**, the same scan during scene/asset load can overflow the native stack and **crash the player process during loading** with no managed exception, leaving only a `SymGetSymFromAddr64` / `RtlUserThreadStart` stack in `Player.log`. The depth-limit warning is the **only managed-side hint** that the cycle exists — and it's easy to ignore because the editor "works."

This is **build-vs-editor divergence**, not a runtime-state bug: the recursion is in the **type graph**, not the runtime instance graph, so it triggers even when the offending field is empty at runtime.

## Symptom
- Editor: yellow warning `Serialization depth limit 10 exceeded at '<Class>.<field>'. There may be an object composition cycle in one or more of your serialized classes. Consider rearranging data or use [SerializeReference].` Game still runs.
- Standalone build: process crashes during the loading screen / scene change that exercises the offending type chain. `Player.log` ends with a native crash (`SymGetSymFromAddr64` errors, raw addresses, `RtlUserThreadStart`), **no managed exception line** — but the depth-limit warning still appears earlier in the same log, with a stack pointing into the code path that triggered the load.
- The user-reported repro that surfaced this: `CharacterSelectPanel.OnSelectClicked` → `GameLauncher.LaunchSolo` → `StartCoroutine(LaunchSequence)` → scene loads `GameScene` → Unity scans `WildernessZone` MonoBehaviours → walks `_wildlife` → `HibernatedNPCData.ProfileData` → `CharacterProfileSaveData.partyMembers` → `CharacterProfileSaveData.partyMembers` → … → depth 10 → stack overflow.

## Root cause
A `[Serializable]` data class held a `List<>` of itself, used as the persistence DTO for the [[party]] system:

```csharp
[System.Serializable]
public class CharacterProfileSaveData
{
    public string characterGuid;
    public Dictionary<string, string> componentStates;  // OK — Unity can't serialize Dictionary; Newtonsoft owns it
    public List<CharacterProfileSaveData> partyMembers; // ← SELF-CYCLE — Unity walks the type recursively
    public List<WorldAssociation> worldAssociations;
}
```

A second `[Serializable]` class (`HibernatedNPCData`) referenced it as a plain field:

```csharp
[Serializable]
public class HibernatedNPCData
{
    public string CharacterId;
    public CharacterProfileSaveData ProfileData; // entry point into the cycle
    // ...
}
```

A `MonoBehaviour` (`WildernessZone : NetworkBehaviour`) then held a `[SerializeField] List<HibernatedNPCData>`, which is what dragged the whole chain into Unity's serialization scanner at scene-load time. The depth-10 limit was hit on every scan; in the editor it just logs, in the Mono build the native walk overflowed during `GameLauncher.LaunchSequence` and the process died.

The class was **never intended** to be Unity-serialized — its own file comment said "Serialized via Newtonsoft.Json (Dictionary not Unity-Inspector-serializable)." But `[System.Serializable]` is a generic .NET attribute that Unity also reacts to, so the moment a path from a `[SerializeField]` reached the type, Unity's serializer started walking it.

## How to avoid
**Rule of thumb**: any `[Serializable]` DTO that has a field of its own type (directly, through `List<T>`, or through an array) — or that participates in an indirect cycle (A→B→A) — **must** have that field annotated with **either** `[System.NonSerialized]` (if Unity shouldn't see it at all) **or** `[SerializeReference]` (if you genuinely want Unity to handle it as a reference, not by value).

Decision tree:

- The DTO is round-tripped by **Newtonsoft.Json** (project default — see [SaveFileHandler.cs](../../Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs)) and never read from the Unity Inspector → `[System.NonSerialized]` is the right call. Newtonsoft uses default `DefaultContractResolver` with `IgnoreSerializableAttribute = true`, which **ignores** `[NonSerialized]` and still serializes the field by property/field convention. Persistence is preserved; Unity stops walking the type.
- The DTO is used through a Unity Inspector (`[SerializeField]` on a `MonoBehaviour` / `ScriptableObject`) and the cycle is genuinely "this is a reference, not embedded value" → use `[SerializeReference]`. This is heavier and only matches a small set of designs (e.g. authoring a node graph in the Inspector).
- A field that holds a back-pointer to a parent / leader → store the parent **by id** (`string parentGuid`) and resolve at runtime. The DTO stays acyclic by construction.

**When in doubt, audit every `[Serializable]` class for self-cycles before any new Unity-serialized container references it.** The shape to look for:

```csharp
[Serializable]
public class Foo {
    public Foo parent;          // direct self-cycle
    public List<Foo> children;  // self-cycle via list
    public Foo[] siblings;      // self-cycle via array
    public List<Bar> bars;      // OK only if Bar doesn't reach Foo
}
```

An indirect cycle (A holds List<B>, B holds List<A>) explodes identically. The depth-limit warning will name whichever field finally hits depth 10, which may not be the "root" of the cycle — read the full `Serialization hierarchy:` block, not just the first line.

## How to fix (if already hit)
Add `[System.NonSerialized]` (or, where appropriate, `[SerializeReference]`) to the cycling field. The 2026-05-15 patch:

```csharp
// CharacterProfileSaveData.cs
[System.NonSerialized]
public List<CharacterProfileSaveData> partyMembers = new List<CharacterProfileSaveData>();
```

What this changes:
- Unity's serializer no longer walks `partyMembers` when it scans any container that reaches `CharacterProfileSaveData` — the depth-10 warning goes away in the editor and the native crash goes away in builds.
- Newtonsoft's default `JsonConvert.SerializeObject` / `DeserializeObject` still round-trip the field (default `IgnoreSerializableAttribute = true` means `[NonSerialized]` is ignored by the resolver). Verified by `SaveFileHandler.WriteProfileAsync` / `ReadProfileAsync` continuing to write `partyMembers` into the JSON profile.

Existing `Profiles/{characterGuid}.json` saves are byte-compatible — the field name and shape on disk did not change.

A complementary cleanup that did **not** ship in this patch (and may be a follow-up):
- Use a list of GUIDs (`List<string> partyMemberGuids`) and resolve at load time. Eliminates the cycle by construction and removes a future risk: if Newtonsoft ever encounters a runtime cycle (e.g. a future bug that adds a leader as their own party member), default settings (no `ReferenceLoopHandling`) will infinite-recurse and stack-overflow at JSON write time too.
- Set `ReferenceLoopHandling = ReferenceLoopHandling.Ignore` + a bounded `MaxDepth` on the `JsonSerializerSettings` used by `SaveFileHandler`.

## Diagnostic recipe
On any "editor works, build crashes during loading" report:

1. Open `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\Player.log` (the most recent run is the live file; previous runs are `Player-prev.log`).
2. Search for `Serialization depth limit` — if it appears, you have a type-graph cycle. The hierarchy block names every level; the bottom-most line names the entry point (often a `[SerializeField]` on a `MonoBehaviour`).
3. Even if the crash trace at the very end is pure native (`SymGetSymFromAddr64`, raw addresses), the depth-limit warning is the actionable signal — the native crash is a downstream symptom.

## Affected systems
- [[save-load]]
- [[party]]
- [[world]]
- [[character]]

## Links
- [[save-load]] — `CharacterProfileSaveData` is the portable character DTO owned by Save / Load.
- [[party]] — uses the same DTO to persist party membership across worlds.
- [[host-only-state-blindspot]] — sibling class of "feature passes in editor, fails in build/late-join" bugs.

## Sources
- 2026-05-15 conversation with [[kevin]] — "Build crashes during loading after clicking a character."
- 2026-05-15 `Player.log` excerpt — depth-limit warning + native crash tail.
- [CharacterProfileSaveData.cs](../../Assets/Scripts/Core/SaveLoad/CharacterProfileSaveData.cs).
- [MapSaveData.cs](../../Assets/Scripts/World/MapSystem/MapSaveData.cs) — `HibernatedNPCData.ProfileData`.
- [WildernessZone.cs](../../Assets/Scripts/World/Zones/WildernessZone.cs) — `_wildlife : List<HibernatedNPCData>`.
- [SaveFileHandler.cs](../../Assets/Scripts/Core/SaveLoad/SaveFileHandler.cs) — default Newtonsoft settings.
- [.agent/skills/save-load-system/SKILL.md](../../.agent/skills/save-load-system/SKILL.md) — operational procedures.
