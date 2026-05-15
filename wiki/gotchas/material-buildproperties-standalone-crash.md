---
type: gotcha
title: "Material::BuildProperties native crash — Editor tolerates, standalone Mono segfaults at scene load"
tags: [build, standalone, mono, material, shader, urp, native-crash, scene-load, recurring-pattern, diagnostics]
created: 2026-05-16
updated: 2026-05-16
sources:
  - "[Assets/Materials/M_ConstructionCurtain.mat](../../Assets/Materials/M_ConstructionCurtain.mat)"
  - "[Assets/Scripts/World/Buildings/Building.cs](../../Assets/Scripts/World/Buildings/Building.cs)"
  - "2026-05-15/16 — 5+ hour debugging session with Kevin (standalone build crash after `f5abe296`)"
related:
  - "[[building]]"
  - "[[construction]]"
  - "[[network]]"
status: mitigated
confidence: medium
---

# Material::BuildProperties native crash — Editor tolerates, standalone Mono segfaults at scene load

## Summary
A Material asset that the Editor happily loads (warning at worst) can crash the **standalone Mono build natively inside `UnityPlayer.dll`** during scene load, before any user code runs. The crash always lands the same way: `ShaderPropertySheet::UpdateTextureInfo → SetTextureWithPlacement → UnityPropertySheet::AssignDefinedPropertiesTo → Material::BuildProperties → AwakeFromLoadQueue::PersistentManagerAwakeSingleObject → LoadSceneOperation::IntegrateTimeSliced → PreloadManager::UpdatePreloading`. There is **no managed exception**, just `Crash!!!` in `Player.log`. This is the load-time equivalent of "magenta material in editor → segfault in build," and it eats hours of bisect time because the Editor reports nothing wrong.

## Symptom

`Player.log`:
```
Loading scene 'GameScene'...
Crash!!!

========== OUTPUTTING STACK TRACE ==================
0x... UnityPlayer.dll ShaderPropertySheet::UpdateTextureInfo
0x... UnityPlayer.dll ShaderPropertySheet::SetTextureWithPlacement
0x... UnityPlayer.dll UnityPropertySheet::AssignDefinedPropertiesTo
0x... UnityPlayer.dll Material::BuildProperties
0x... UnityPlayer.dll AwakeFromLoadQueue::PersistentManagerAwakeSingleObject
0x... UnityPlayer.dll PersistentManager::IntegrateObjectAndUnlockIntegrationMutexInternal
0x... UnityPlayer.dll LoadSceneOperation::IntegrateTimeSliced
0x... UnityPlayer.dll PreloadManager::UpdatePreloadingSingleStep
0x... UnityPlayer.dll PreloadManager::UpdatePreloading
0x... UnityPlayer.dll ExecutePlayerLoop
0x... UnityPlayer.dll UnityMain
========== END OF STACKTRACE ===========
```

**Without** the UnityPlayer PDB next to UnityPlayer.dll, every line above is `(function-name not available)` — that's the case that wastes the most time.

Other surface signals the Editor *might* show:
- The material's Inspector lists keywords under `m_InvalidKeywords` (shader doesn't declare them).
- `Infinity` values appear in the material's YAML (`_CameraFadeParams: {r: 0, g: Infinity, b: 0, a: 0}`).
- "magenta material" or "shader without GPU deformation support" warnings in Console.

But the Editor will still let you press Play, see the build "succeed," and never block you. Only the standalone player crashes.

## Root cause

Unity Editor and standalone Mono build use **different material serialization tolerances**:

| Path | Tolerance |
|---|---|
| Editor (`AssetDatabase.LoadAssetAtPath`) | Lazy resolution. Missing texture refs → null + Console warning. Property/shader mismatches → silent. Continues running. |
| Standalone build (`PersistentManager::IntegrateObjectAndUnlockIntegrationMutexInternal` → `Material::BuildProperties`) | Strict. Native loader iterates declared shader properties, calls `UpdateTextureInfo` for each Texture-typed property. If the material's stored property data for that slot is in any way inconsistent (stripped shader variant, dangling texture, type mismatch, invalid keyword that maps to a stripped property), the native code dereferences a bad pointer → SIGSEGV. |

The build's `Resources/` folder and any asset reachable from scenes-in-build are pre-baked into the data files at build time. **Any single bad material in that chain takes down the whole boot.** Removing the *consumers* of the material from the scene removes the chain — which is why scene-level GameObject bisect "fixes" the crash without identifying it.

The May 2026 incident: `f5abe296` added `Assets/Materials/M_ConstructionCurtain.mat` (URP Particles/Unlit) wired into `Building._constructionCurtainMaterial` on 7 networked building prefabs in `DefaultNetworkPrefabs.asset`. The material's exact corruption was never pinpointed — `m_InvalidKeywords: [_FLIPBOOKBLENDING_OFF]` and `_CameraFadeParams: {r: 0, g: Infinity, b: 0, a: 0}` were both flagged as candidates, neither was confirmed as the trigger. Removing the build's reference path (nulling the field on the 7 prefabs) sidestepped the crash without explaining it.

## How to diagnose (PDB-first, mandatory)

**Step 1 — symbolicate before anything else.** Without managed function names in the stack trace, you'll burn hours guessing categories. Five minutes of setup saves the day:

```powershell
# Copy Unity's development-mode UnityPlayer symbols to the build folder
$pdbSrc = "C:\Program Files\Unity\Hub\Editor\<UNITY_VERSION>\Editor\Data\PlaybackEngines\WindowsStandaloneSupport\Variations\win64_player_development_mono\UnityPlayer_Win64_player_development_mono_x64.pdb"
$buildDir = "<path to build folder containing UnityPlayer.dll>"
Copy-Item $pdbSrc -Destination $buildDir
# Also copy UnityCrashHandler64.pdb from the same Variations folder for the crash handler
```

**Critical:** the PDB filename must match the embedded debug record name (`UnityPlayer_Win64_player_development_mono_x64.pdb`, NOT renamed to `UnityPlayer.pdb`). dbghelp checks the GUID inside the PDB against the DLL's debug directory record; renaming defeats it.

Make sure the build is a **Development Build with Script Debugging** enabled (Build Settings → ☑ Development Build, ☑ Script Debugging) so the variation matches the PDB above. Release builds need `WindowsPlayer_player_Release_mono_x64.pdb` from the same `Variations/` folder.

**Step 2 — re-run the existing build (no rebuild needed).** dbghelp at crash time loads the PDB and resolves the addresses.

**Step 3 — read `Player.log`'s `OUTPUTTING STACK TRACE` section.** If you see `ShaderPropertySheet::UpdateTextureInfo → Material::BuildProperties`, you're hitting this gotcha.

## How to fix (if already hit)

Three options, in order of cost:

1. **Cut the build's reference path to the bad material.** Find every consumer of the suspected material (`grep -rln <material-guid> Assets/`), null out the field on each, rebuild. Material isn't pulled into the build → no crash. **This was the May 2026 fix.** Trade-off: any feature relying on that material is disabled.

2. **Recreate the material from scratch.** Right-click in Project window → Create → Material → set the same shader → reconfigure properties via Inspector (do *not* copy from the broken .mat YAML). Re-wire on every consumer. This is the proper fix when you want the feature back.

3. **Bisect among the build's materials.** If you don't know which material is broken, use Unity's build report (`Editor.log` after a clean build → search for `Build Report` → list under `Used Assets and files from the Resources folder` sorted by uncompressed size). Materials are typed `.mat`. Compare against what the 8 culprit GameObjects (from any scene-deletion bisect) transitively reference.

**Process notes from the May 2026 incident** (so future-you doesn't repeat them):
- Don't trust an "incremental" build with asset changes. Unity's `BuildPipeline.BuildPlayer` with no `CleanBuildCache` will report `Succeeded` and skip writing the .exe if it judges nothing material changed. Verify by checking the build folder's `UnityPlayer.dll` mtime updated.
- `git restore Assets/Scenes/GameScene.unity` followed by MCP `scene-open` does NOT discard Unity's in-memory unsaved state. The Editor will silently overwrite your restored file on next save. To force a true reload: close the scene without saving via `EditorSceneManager.CloseScene(scene, removeScene: true)`, then `OpenScene(..., Single)`.
- Disabling GameObjects (`SetActive(false)`) does NOT prevent their deserialization at scene load. The native loader deserializes every object in the scene file regardless of `m_IsActive`. Only **deleting** the object cuts the chain.
- The user's first bisect identifying `f5abe296` was correct, but the broken Resources/ SOs found en route (Fireball / BurnDoT / PoisonedDoT) were red herrings that ate 2+ hours.

## Affected systems
- [[building]] — `Building._constructionCurtainMaterial` was the field that loaded M_ConstructionCurtain into the build
- [[construction]] — construction-curtain ParticleSystem feature is currently disabled (waiting on a clean material rebuild)
- [[network]] — the 7 prefabs are in `DefaultNetworkPrefabsList`; NGO's preload chain is what included the bad material

## Links
- [[building]]
- [[construction]]
- [[network]]

## Sources
- `Assets/Materials/M_ConstructionCurtain.mat` — the specific corrupt material
- `Assets/Scripts/World/Buildings/Building.cs` — `_constructionCurtainMaterial` serialized field, lines around 600–700
- 7 building prefabs whose `_constructionCurtainMaterial` field is now `{fileID: 0}` (Forge / Farming Building / Lumberyard / Shop / Transporter Building / House prefab / Small house)
- 2026-05-15/16 conversation with Kevin — 5+ hour debugging session that produced this gotcha
