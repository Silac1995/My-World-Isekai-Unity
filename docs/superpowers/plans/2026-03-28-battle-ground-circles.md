# Battle Ground Circle Indicators — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display blue (ally) and red (enemy) ground circles beneath every character in a battle, visible only to the local player, using URP Decal Projectors.

**Architecture:** Two new scripts (`BattleGroundCircle.cs` per-circle component, `BattleCircleManager.cs` orchestrator extending `CharacterSystem`), two new events on existing classes, a Shader Graph decal ring shader, two shared materials, and one prefab. Local-only rendering — no network traffic.

**Tech Stack:** Unity URP (DecalProjector, Shader Graph), Unity Netcode for GameObjects (`CharacterSystem` / `NetworkBehaviour`), C#

**Spec:** `docs/superpowers/specs/2026-03-28-battle-ground-circles-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs` | **Modify** | Add `OnBattleJoined` event, fire in `JoinBattle()` |
| `Assets/Scripts/BattleManager/BattleManager.cs` | **Modify** | Add `OnParticipantAdded` event, fire in `RegisterCharacter()` |
| `Assets/Scripts/BattleManager/BattleGroundCircle.cs` | **Create** | Per-character circle: manages one `DecalProjector`, fade-in/out, dim/restore |
| `Assets/Scripts/BattleManager/BattleCircleManager.cs` | **Create** | Orchestrator: spawns/tracks/destroys circles for all battle participants |
| `Assets/Scripts/Character/Character.cs` | **Modify** | Add `BattleCircleManager` SerializeField + property + Awake fallback |
| `Assets/Shaders/BattleGroundCircle.shadergraph` | **Create** | URP Decal Shader Graph — procedural ring with pulse animation |
| `Assets/Materials/BattleGroundCircle_Ally_Mat.mat` | **Create** | Shared blue ally material |
| `Assets/Materials/BattleGroundCircle_Enemy_Mat.mat` | **Create** | Shared red enemy material |
| `Assets/Prefabs/BattleGroundCircle.prefab` | **Create** | DecalProjector + BattleGroundCircle.cs |
| `Assets/Settings/PC_Renderer.asset` | **Modify** | Enable Decal Renderer Feature |
| `Assets/Settings/Mobile_Renderer.asset` | **Modify** | Enable Decal Renderer Feature |
| Character prefabs in `Assets/Prefabs/Character/` | **Modify** | Add BattleCircleManager child GameObject |

---

### Task 1: Add `OnBattleJoined` Event to CharacterCombat

**Files:**
- Modify: `Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs:19` (event declaration), `:439-446` (JoinBattle method)

- [ ] **Step 1: Add event declaration**

In `CharacterCombat.cs`, after line 19 (`public event Action OnBattleLeft;`), add:

```csharp
public event Action<BattleManager> OnBattleJoined;
```

- [ ] **Step 2: Fire event in JoinBattle()**

In `CharacterCombat.cs`, at the end of `JoinBattle()` (after line 445), add:

```csharp
OnBattleJoined?.Invoke(manager);
```

The full method becomes:
```csharp
public void JoinBattle(BattleManager manager)
{
    _currentBattleManager = manager;
    ChangeCombatMode(true);

    // Passive trigger: OnBattleStart
    _character.CharacterAbilities?.OnPassiveTriggerEvent(PassiveTriggerCondition.OnBattleStart, _character, null);

    OnBattleJoined?.Invoke(manager);
}
```

**Why after passives:** The event fires after all internal state is set (`_currentBattleManager` assigned, combat mode on). Subscribers like `BattleCircleManager` can safely read `CurrentBattleManager` and iterate teams.

**Note:** `JoinBattle` is called from `RegisterCharacter` which runs on ALL clients (server path via `AddParticipantInternal → RegisterCharacter`, client path via `AddParticipantClientRpc → RegisterCharacter`). The `IsOwner` guard on `BattleCircleManager` scopes the response to the local player only.

- [ ] **Step 3: Verify compilation**

Run: Use `assets-refresh` MCP tool or `console-get-logs` to check for compilation errors.
Expected: No errors — the event is additive, no existing code affected.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Character/CharacterCombat/CharacterCombat.cs
git commit -m "feat(combat): add OnBattleJoined event to CharacterCombat"
```

---

### Task 2: Add `OnParticipantAdded` Event to BattleManager

**Files:**
- Modify: `Assets/Scripts/BattleManager/BattleManager.cs:35-37` (properties area), `:232-259` (RegisterCharacter method)

- [ ] **Step 1: Add event declaration**

In `BattleManager.cs`, after line 37 (`public CombatEngagementCoordinator Coordinator => _engagementCoordinator;`), add:

```csharp
public event Action<Character> OnParticipantAdded;
```

- [ ] **Step 2: Fire event at the end of RegisterCharacter()**

In `BattleManager.cs`, at the end of `RegisterCharacter()` (after the Debug.Log on line 258, before the closing `}`), add:

```csharp
OnParticipantAdded?.Invoke(character);
```

**Spec deviation note:** The spec says to fire this in `AddParticipantInternal()`, but that method is server-only (called from `AddParticipant` which guards `if (!IsServer) return;`). On non-host clients, `AddParticipantClientRpc` calls `RegisterCharacter` directly, bypassing `AddParticipantInternal`. Firing in `RegisterCharacter()` ensures the event fires on ALL clients — which is required for the local-player `BattleCircleManager` to receive mid-battle joiner notifications.

- [ ] **Step 3: Clean up event subscriptions in OnDestroy**

In `BattleManager.cs`, the existing `OnDestroy()` (line 504) already cleans up character events. No additional cleanup needed for `OnParticipantAdded` — subscribers (`BattleCircleManager`) unsubscribe themselves in `HandleBattleLeft()`.

- [ ] **Step 4: Verify compilation**

Run: Use `assets-refresh` MCP tool.
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/BattleManager/BattleManager.cs
git commit -m "feat(combat): add OnParticipantAdded event to BattleManager"
```

---

### Task 3: Create `BattleGroundCircle.cs`

**Files:**
- Create: `Assets/Scripts/BattleManager/BattleGroundCircle.cs`

- [ ] **Step 1: Create the script**

Use `script-update-or-create` MCP tool to create `Assets/Scripts/BattleManager/BattleGroundCircle.cs`:

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Manages a single ground circle indicator beneath a character during battle.
/// Instantiated as a prefab, parented to the character's root transform.
/// Lifecycle managed exclusively by BattleCircleManager.
/// </summary>
public class BattleGroundCircle : MonoBehaviour
{
    [SerializeField] private DecalProjector _decalProjector;

    private Coroutine _fadeCoroutine;
    private bool _isCleaningUp;

    private const float FADE_DURATION = 0.3f;
    private const float DIM_FADE_FACTOR = 0.25f;

    /// <summary>
    /// Assigns the shared material and starts fade-in.
    /// Called once by BattleCircleManager after instantiation.
    /// </summary>
    public void Initialize(Material material)
    {
        if (_decalProjector == null)
            _decalProjector = GetComponent<DecalProjector>();

        _decalProjector.material = material;
        _decalProjector.fadeFactor = 0f;

        _fadeCoroutine = StartCoroutine(FadeTo(1f));
    }

    /// <summary>
    /// Reduces opacity for incapacitated characters. Circle stays visible but faded.
    /// </summary>
    public void Dim()
    {
        if (_isCleaningUp) return;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeTo(DIM_FADE_FACTOR));
    }

    /// <summary>
    /// Restores full opacity for revived characters.
    /// </summary>
    public void Restore()
    {
        if (_isCleaningUp) return;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeTo(1f));
    }

    /// <summary>
    /// Fade-out then self-destruct. Guarded against double-calls.
    /// </summary>
    public void Cleanup()
    {
        if (_isCleaningUp) return;
        _isCleaningUp = true;
        StopActiveFade();
        _fadeCoroutine = StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeTo(float targetFade)
    {
        if (_decalProjector == null) yield break;

        float startFade = _decalProjector.fadeFactor;
        float elapsed = 0f;

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / FADE_DURATION);

            if (_decalProjector == null) yield break;
            _decalProjector.fadeFactor = Mathf.Lerp(startFade, targetFade, t);

            yield return null;
        }

        if (_decalProjector != null)
            _decalProjector.fadeFactor = targetFade;
    }

    private IEnumerator FadeOutAndDestroy()
    {
        if (_decalProjector != null)
        {
            float startFade = _decalProjector.fadeFactor;
            float elapsed = 0f;

            while (elapsed < FADE_DURATION)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FADE_DURATION);

                if (_decalProjector == null) break;
                _decalProjector.fadeFactor = Mathf.Lerp(startFade, 0f, t);

                yield return null;
            }
        }

        Destroy(gameObject);
    }

    private void StopActiveFade()
    {
        if (_fadeCoroutine != null)
        {
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        StopActiveFade();
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: Use `console-get-logs` MCP tool.
Expected: No errors. `DecalProjector` requires `using UnityEngine.Rendering.Universal;` — included above.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/BattleManager/BattleGroundCircle.cs
git commit -m "feat(combat): add BattleGroundCircle per-character circle component"
```

---

### Task 4: Create `BattleCircleManager.cs`

**Files:**
- Create: `Assets/Scripts/BattleManager/BattleCircleManager.cs`

- [ ] **Step 1: Create the script**

Use `script-update-or-create` MCP tool to create `Assets/Scripts/BattleManager/BattleCircleManager.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Local-player orchestrator for battle ground circle indicators.
/// Extends CharacterSystem — lives on a dedicated child GameObject of the Character prefab.
/// Only activates on the owning client (IsOwner guard).
/// </summary>
public class BattleCircleManager : CharacterSystem
{
    [Header("Battle Circle Settings")]
    [SerializeField] private GameObject _battleCirclePrefab;
    [SerializeField] private Material _allyMaterial;
    [SerializeField] private Material _enemyMaterial;

    private readonly Dictionary<Character, BattleGroundCircle> _activeCircles = new();
    private BattleManager _cachedBattleManager;

    #region Lifecycle

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_character != null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnBattleJoined += HandleBattleJoined;
            _character.CharacterCombat.OnBattleLeft += HandleBattleLeft;
        }
    }

    protected override void OnDisable()
    {
        if (_character != null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnBattleJoined -= HandleBattleJoined;
            _character.CharacterCombat.OnBattleLeft -= HandleBattleLeft;
        }
        CleanupAll();
        base.OnDisable();
    }

    #endregion

    #region Battle Events

    private void HandleBattleJoined(BattleManager manager)
    {
        if (!IsOwner) return;

        // Defensive: clear any leftover circles from a prior battle (rapid re-engagement)
        CleanupAll();

        _cachedBattleManager = manager;

        // Resolve local player's team
        BattleTeam localTeam = manager.BattleTeams.FirstOrDefault(t => t.IsAlly(_character));
        if (localTeam == null)
        {
            Debug.LogError($"<color=red>[BattleCircleManager]</color> Could not resolve local team for {_character.CharacterName}");
            return;
        }

        // Spawn circles for all characters in both teams
        foreach (BattleTeam team in manager.BattleTeams)
        {
            foreach (Character character in team.CharacterList)
            {
                SpawnCircleFor(character, localTeam.IsAlly(character));
            }
        }

        // Subscribe to mid-battle joiners
        manager.OnParticipantAdded += HandleParticipantAdded;
    }

    private void HandleBattleLeft()
    {
        if (!IsOwner) return;

        // Unsubscribe from cached BattleManager (null-safe — BattleManager may be destroyed)
        if (_cachedBattleManager != null)
        {
            _cachedBattleManager.OnParticipantAdded -= HandleParticipantAdded;
        }

        CleanupAll();
        _cachedBattleManager = null;
    }

    private void HandleParticipantAdded(Character newParticipant)
    {
        if (!IsOwner || _cachedBattleManager == null) return;
        if (_activeCircles.ContainsKey(newParticipant)) return;

        BattleTeam localTeam = _cachedBattleManager.BattleTeams.FirstOrDefault(t => t.IsAlly(_character));
        if (localTeam == null) return;

        SpawnCircleFor(newParticipant, localTeam.IsAlly(newParticipant));
    }

    #endregion

    #region Per-Character Events

    private void HandleCharacterIncapacitated(Character character)
    {
        if (!IsOwner) return;
        if (_activeCircles.TryGetValue(character, out BattleGroundCircle circle))
        {
            circle.Dim();
        }
    }

    private void HandleCharacterWakeUp(Character character)
    {
        if (!IsOwner) return;
        if (_activeCircles.TryGetValue(character, out BattleGroundCircle circle))
        {
            circle.Restore();
        }
    }

    #endregion

    #region Circle Management

    private void SpawnCircleFor(Character target, bool isAlly)
    {
        if (target == null || _battleCirclePrefab == null) return;
        if (_activeCircles.ContainsKey(target)) return;

        // Parent to character's root transform (not visual transform — avoids sprite flip issues)
        GameObject circleGO = Instantiate(_battleCirclePrefab, target.transform);
        circleGO.transform.localPosition = Vector3.zero;
        circleGO.transform.localRotation = Quaternion.identity;

        BattleGroundCircle circle = circleGO.GetComponent<BattleGroundCircle>();
        Material material = isAlly ? _allyMaterial : _enemyMaterial;
        circle.Initialize(material);

        _activeCircles[target] = circle;

        // Subscribe to incapacitated/wakeup for this specific character
        target.OnIncapacitated += HandleCharacterIncapacitated;
        target.OnWakeUp += HandleCharacterWakeUp;

        // If character is already incapacitated at spawn time, dim immediately
        if (target.IsIncapacitated)
        {
            circle.Dim();
        }
    }

    private void CleanupAll()
    {
        foreach (var kvp in _activeCircles)
        {
            if (kvp.Key != null)
            {
                kvp.Key.OnIncapacitated -= HandleCharacterIncapacitated;
                kvp.Key.OnWakeUp -= HandleCharacterWakeUp;
            }

            if (kvp.Value != null)
            {
                kvp.Value.Cleanup();
            }
        }

        _activeCircles.Clear();
    }

    #endregion
}
```

**Design notes:**
- Extends `CharacterSystem` → inherits `_character` reference, `IsOwner`, and auto-subscription to the local character's `OnIncapacitated`/`OnWakeUp` via base class. However, this component manages circles for ALL battle participants, so it subscribes to each tracked character's events individually in `SpawnCircleFor()`.
- The base class `HandleIncapacitated`/`HandleWakeUp` virtual overrides are intentionally NOT used — those only fire for the local character. The per-character subscriptions (`HandleCharacterIncapacitated`/`HandleCharacterWakeUp`) handle all characters.
- `OnDisable` calls `CleanupAll()` BEFORE `base.OnDisable()` to ensure circle cleanup happens while character references are still valid.

- [ ] **Step 2: Verify compilation**

Run: Use `console-get-logs` MCP tool.
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/BattleManager/BattleCircleManager.cs
git commit -m "feat(combat): add BattleCircleManager local-player orchestrator"
```

---

### Task 5: Integrate BattleCircleManager into Character.cs

**Files:**
- Modify: `Assets/Scripts/Character/Character.cs:70` (SerializeField), `:164` (property), `:328` (Awake fallback)

- [ ] **Step 1: Add SerializeField**

In `Character.cs`, after line 70 (`[SerializeField] private CharacterBookKnowledge _characterBookKnowledge;`), before the `#endregion`, add:

```csharp
[SerializeField] private BattleCircleManager _battleCircleManager;
```

- [ ] **Step 2: Add public property**

In `Character.cs`, after line 164 (`public CharacterBookKnowledge CharacterBookKnowledge => _characterBookKnowledge;`), add:

```csharp
public BattleCircleManager BattleCircleManager => _battleCircleManager;
```

- [ ] **Step 3: Add Awake() fallback**

In `Character.cs`, after line 328 (`if (_characterBookKnowledge == null) _characterBookKnowledge = GetComponent<CharacterBookKnowledge>();`), add:

```csharp
if (_battleCircleManager == null) _battleCircleManager = GetComponentInChildren<BattleCircleManager>();
```

**Note:** Uses `GetComponentInChildren` (not `GetComponent`) because `BattleCircleManager` lives on a child GameObject per the Facade + Child Hierarchy pattern.

- [ ] **Step 4: Verify compilation**

Run: Use `console-get-logs` MCP tool.
Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Character/Character.cs
git commit -m "feat(character): expose BattleCircleManager on Character facade"
```

---

### Task 6: Enable Decal Renderer Feature on URP Renderers

**Files:**
- Modify: `Assets/Settings/PC_Renderer.asset`
- Modify: `Assets/Settings/Mobile_Renderer.asset`

DecalProjector requires the Decal Renderer Feature to be active on the URP Renderer asset.

- [ ] **Step 1: Check if Decal Renderer Feature exists**

Use `script-execute` MCP tool:

```csharp
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public class CheckDecalFeature
{
    public static string Execute()
    {
        string[] rendererPaths = new[] {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset"
        };

        string result = "";
        foreach (string path in rendererPaths)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null)
            {
                result += $"{path}: NOT FOUND\n";
                continue;
            }

            bool hasDecal = false;
            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature is DecalRendererFeature)
                {
                    hasDecal = true;
                    break;
                }
            }
            result += $"{path}: Decal Feature = {(hasDecal ? "ENABLED" : "MISSING")}\n";
        }
        return result;
    }
}
```

- [ ] **Step 2: Add Decal Renderer Feature if missing**

If either renderer is missing the feature, use `script-execute`:

```csharp
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public class AddDecalFeature
{
    public static string Execute()
    {
        string[] rendererPaths = new[] {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset"
        };

        string result = "";
        foreach (string path in rendererPaths)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null) continue;

            bool hasDecal = false;
            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature is DecalRendererFeature) { hasDecal = true; break; }
            }

            if (!hasDecal)
            {
                var decalFeature = ScriptableObject.CreateInstance<DecalRendererFeature>();
                decalFeature.name = "Decal";
                AssetDatabase.AddObjectToAsset(decalFeature, renderer);
                renderer.rendererFeatures.Add(decalFeature);
                EditorUtility.SetDirty(renderer);
                result += $"{path}: Decal Feature ADDED\n";
            }
            else
            {
                result += $"{path}: Already has Decal Feature\n";
            }
        }

        AssetDatabase.SaveAssets();
        return result;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Settings/PC_Renderer.asset Assets/Settings/Mobile_Renderer.asset
git commit -m "feat(rendering): enable Decal Renderer Feature on URP renderers"
```

---

### Task 7: Create Shader Graph, Materials, and Prefab

This task creates the visual assets via Unity MCP tools.

#### Sub-task 7a: Create the Decal Shader Graph

- [ ] **Step 1: Create the Shader Graph asset**

Use `script-execute` MCP tool to programmatically create a URP Decal Shader Graph:

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.ShaderGraph;
using System.IO;

public class CreateBattleCircleShader
{
    public static string Execute()
    {
        string shaderPath = "Assets/Shaders/BattleGroundCircle.shadergraph";

        // Check if file already exists
        if (AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) != null)
            return "Shader already exists at " + shaderPath;

        // Ensure directory exists
        string dir = Path.GetDirectoryName(shaderPath);
        if (!AssetDatabase.IsValidFolder(dir))
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", dir));

        // Create a minimal Decal Shader Graph via the menu item approach
        // Unfortunately ShaderGraph API is internal, so we create via a template
        // Instead, create a simple shader file as a fallback

        string shaderCode = @"
Shader ""Custom/BattleGroundCircle""
{
    Properties
    {
        [HDR] _Color (""Color"", Color) = (1, 1, 1, 1)
        _InnerRadius (""Inner Radius"", Range(0, 1)) = 0.3
        _OuterRadius (""Outer Radius"", Range(0, 1)) = 0.5
        _Softness (""Softness"", Range(0, 1)) = 0.05
        _PulseSpeed (""Pulse Speed"", Float) = 0.0
        _PulseIntensity (""Pulse Intensity"", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { ""RenderType""=""Transparent"" ""Queue""=""Transparent+1"" ""RenderPipeline""=""UniversalPipeline"" }
        LOD 100

        Pass
        {
            Name ""BattleCircleDecal""
            Tags { ""LightMode""=""UniversalForward"" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _InnerRadius;
                half _OuterRadius;
                half _Softness;
                half _PulseSpeed;
                half _PulseIntensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 center = float2(0.5, 0.5);
                float dist = distance(input.uv, center) * 2.0;

                float inner = smoothstep(_InnerRadius - _Softness, _InnerRadius + _Softness, dist);
                float outer = 1.0 - smoothstep(_OuterRadius - _Softness, _OuterRadius + _Softness, dist);
                float ring = inner * outer;

                float pulse = 1.0 - (_PulseIntensity * (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5));

                half4 col = _Color;
                col.a = ring * pulse;

                clip(col.a - 0.01);
                return col;
            }
            ENDHLSL
        }
    }
}
";

        File.WriteAllText(Path.Combine(Application.dataPath, "".."", shaderPath), shaderCode);
        AssetDatabase.Refresh();

        return ""Created shader at "" + shaderPath + "". NOTE: This is a standard URP shader. If DecalProjector requires a Shader Graph Decal target, create a Shader Graph manually: Right-click in Shaders folder → Create → Shader Graph → URP → Decal Shader Graph, then recreate the ring logic as nodes."";
    }
}
```

**Important fallback:** The `DecalProjector` component **requires** a shader with the Decal shader target. If the hand-written shader above doesn't work with `DecalProjector`, you must create the Shader Graph manually in Unity:

1. Right-click `Assets/Shaders/` → Create → Shader Graph → URP → Decal Shader Graph
2. Name it `BattleGroundCircle`
3. Add properties: `_Color` (Color HDR), `_InnerRadius` (Float 0.3), `_OuterRadius` (Float 0.5), `_Softness` (Float 0.05), `_PulseSpeed` (Float 0), `_PulseIntensity` (Float 0.2)
4. Build the ring logic:
   - UV node → subtract (0.5, 0.5) → Length → multiply by 2 = `dist`
   - Smoothstep(`_InnerRadius - _Softness`, `_InnerRadius + _Softness`, `dist`) = inner
   - 1 - Smoothstep(`_OuterRadius - _Softness`, `_OuterRadius + _Softness`, `dist`) = outer
   - inner × outer = ring mask
   - Time node → multiply `_PulseSpeed` → Sine → multiply `_PulseIntensity` → subtract from 1 = pulse
   - ring × pulse → Alpha output
   - `_Color` → Base Color output
5. Save the Shader Graph

- [ ] **Step 2: Verify shader compiles**

Use `assets-find` to locate the shader, then `assets-shader-get-data` to check for compilation errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Shaders/BattleGroundCircle*
git commit -m "feat(rendering): add BattleGroundCircle decal ring shader"
```

#### Sub-task 7b: Create Materials

- [ ] **Step 4: Create ally material (blue)**

Use `assets-material-create` MCP tool:
- Path: `Assets/Materials/BattleGroundCircle_Ally_Mat.mat`
- Shader: Use the shader name from Step 2 (either `Custom/BattleGroundCircle` or `Shader Graphs/BattleGroundCircle`)

Then use `assets-modify` to set:
- `_Color`: HDR Blue `(0.2, 0.5, 1.0, 0.7)`
- `_InnerRadius`: `0.3`
- `_OuterRadius`: `0.5`
- `_Softness`: `0.05`
- `_PulseSpeed`: `2.0`
- `_PulseIntensity`: `0.15`

- [ ] **Step 5: Create enemy material (red)**

Use `assets-material-create` MCP tool:
- Path: `Assets/Materials/BattleGroundCircle_Enemy_Mat.mat`
- Same shader as above

Then use `assets-modify` to set:
- `_Color`: HDR Red `(1.0, 0.2, 0.2, 0.7)`
- Same `_InnerRadius`, `_OuterRadius`, `_Softness`, `_PulseSpeed`, `_PulseIntensity` values as ally

- [ ] **Step 6: Commit**

```bash
git add Assets/Materials/BattleGroundCircle_Ally_Mat.mat Assets/Materials/BattleGroundCircle_Enemy_Mat.mat
git commit -m "feat(rendering): add ally (blue) and enemy (red) battle circle materials"
```

#### Sub-task 7c: Create Prefab

- [ ] **Step 7: Create a temporary GameObject in the scene**

Use `gameobject-create` MCP tool to create a new GameObject named `BattleGroundCircle`.

- [ ] **Step 8: Add DecalProjector component**

Use `gameobject-component-add` to add `UnityEngine.Rendering.Universal.DecalProjector` to the GameObject.

- [ ] **Step 9: Configure DecalProjector**

Use `gameobject-component-modify` to set:
- `m_Width`: `2.0`
- `m_Height`: `2.0`
- `m_ProjectionDepth`: `1.0`
- `m_Offset`: `(0, 0, 0)` (centered)
- Rotation: `(90, 0, 0)` on the GameObject transform so it projects downward

- [ ] **Step 10: Add BattleGroundCircle script**

Use `gameobject-component-add` to add `BattleGroundCircle` component.

- [ ] **Step 11: Wire DecalProjector reference**

Use `gameobject-component-modify` on the `BattleGroundCircle` component to set `_decalProjector` to reference the `DecalProjector` on the same GameObject.

- [ ] **Step 12: Save as prefab**

Use `assets-prefab-create` to save the GameObject as `Assets/Prefabs/BattleGroundCircle.prefab`.

- [ ] **Step 13: Clean up scene**

Use `gameobject-destroy` to remove the temporary GameObject from the scene.

- [ ] **Step 14: Commit**

```bash
git add Assets/Prefabs/BattleGroundCircle.prefab
git commit -m "feat(combat): add BattleGroundCircle prefab with DecalProjector"
```

---

### Task 8: Wire BattleCircleManager onto Character Prefabs

**Files:**
- Modify: `Assets/Prefabs/Character/Character_Default.prefab`
- Modify: `Assets/Prefabs/Character/Character_Default_Humanoid.prefab`
- Modify: `Assets/Prefabs/Character/Character_Default_Quadruped.prefab`

Repeat these steps for **each** Character prefab. If one prefab is a variant of another, modifying the base may be sufficient — check with `assets-get-data` first.

- [ ] **Step 1: Check prefab hierarchy**

Use `assets-prefab-open` on `Assets/Prefabs/Character/Character_Default.prefab`, then use `gameobject-find` to understand the existing child hierarchy. Look for existing subsystem child GameObjects (e.g., `CharacterCombat`, `CharacterMovement` children) to follow the same pattern.

- [ ] **Step 2: Create BattleCircleManager child GameObject**

Use `gameobject-create` to create a child GameObject named `BattleCircleManager` under the root Character GameObject.

- [ ] **Step 3: Add BattleCircleManager component**

Use `gameobject-component-add` to add `BattleCircleManager` to the new child GameObject.

- [ ] **Step 4: Configure serialized fields**

Use `gameobject-component-modify` on the `BattleCircleManager` component:
- `_character`: Reference to the root Character component (should auto-resolve via `GetComponentInParent` in Awake, but set explicitly if possible)
- `_battleCirclePrefab`: Reference to `Assets/Prefabs/BattleGroundCircle.prefab`
- `_allyMaterial`: Reference to `Assets/Materials/BattleGroundCircle_Ally_Mat.mat`
- `_enemyMaterial`: Reference to `Assets/Materials/BattleGroundCircle_Enemy_Mat.mat`

- [ ] **Step 5: Wire reference on root Character component**

Use `gameobject-component-modify` on the root `Character` component to set `_battleCircleManager` to reference the new child's `BattleCircleManager` component.

- [ ] **Step 6: Save prefab**

Use `assets-prefab-save` then `assets-prefab-close`.

- [ ] **Step 7: Repeat for remaining Character prefabs**

Repeat Steps 1-6 for `Character_Default_Humanoid.prefab` and `Character_Default_Quadruped.prefab` (unless they are variants of `Character_Default` — in which case, changes propagate automatically).

- [ ] **Step 8: Commit**

```bash
git add Assets/Prefabs/Character/
git commit -m "feat(combat): wire BattleCircleManager onto all Character prefabs"
```

---

### Task 9: Integration Testing

- [ ] **Step 1: Enter Play Mode**

Use `editor-application-set-state` to enter Play Mode with a scene that has characters and a battle system.

- [ ] **Step 2: Trigger a battle**

Initiate combat between the player character and an NPC. Verify in the console (`console-get-logs`) that:
- `[Battle]` logs show characters joining
- No errors related to `BattleCircleManager`, `BattleGroundCircle`, or `DecalProjector`

- [ ] **Step 3: Visual verification**

Use `screenshot-game-view` to capture the Game View and visually verify:
- Blue circles appear under ally characters
- Red circles appear under enemy characters
- Circles follow characters as they move
- Circles fade in smoothly on battle start

- [ ] **Step 4: Test incapacitation**

Let a character get knocked out. Verify:
- Their circle dims (fadeFactor reduced)
- Other circles remain at full opacity

- [ ] **Step 5: Test battle end**

End the battle (defeat all enemies or flee). Verify:
- All circles fade out and are destroyed
- No orphaned GameObjects remain
- No console errors about null references

- [ ] **Step 6: Exit Play Mode**

Use `editor-application-set-state` to exit Play Mode.

- [ ] **Step 7: Commit any hotfixes**

If issues were found and fixed during testing, commit them:

```bash
git add -A
git commit -m "fix(combat): address battle ground circle integration issues"
```

---

### Task 10: Update Combat System Skill File

**Files:**
- Modify: `.agent/skills/combat_system/SKILL.md`

- [ ] **Step 1: Add Battle Ground Circles section**

Append a new section to the combat system skill file after the existing section 11 (Knockback):

```markdown
### 12. Battle Ground Circle Indicators
Visual-only, local-player feature. When the local player joins a battle, colored circles appear on the ground beneath every participant: **Blue** for allies, **Red** for enemies. Colors are relative to the viewer's team.

**Components:**
- **`BattleGroundCircle.cs`** (`Assets/Scripts/BattleManager/`): MonoBehaviour on an instantiated prefab. Manages one `DecalProjector` — handles fade-in on spawn, `Dim()` for incapacitated characters, `Cleanup()` for fade-out + self-destruct. All fade coroutines use `Time.unscaledDeltaTime` (UI-class visual, Rule 24).
- **`BattleCircleManager.cs`** (`Assets/Scripts/BattleManager/`): `CharacterSystem` on a child GameObject of the Character prefab. Only activates for `IsOwner`. Subscribes to `CharacterCombat.OnBattleJoined` / `OnBattleLeft` and `BattleManager.OnParticipantAdded`. Maintains `Dictionary<Character, BattleGroundCircle>`.

**Key events used:**
- `CharacterCombat.OnBattleJoined(BattleManager)` — fired at end of `JoinBattle()`, on all clients
- `CharacterCombat.OnBattleLeft` — fired in `LeaveBattle()`, on all clients
- `BattleManager.OnParticipantAdded(Character)` — fired at end of `RegisterCharacter()`, on all clients
- `Character.OnIncapacitated` / `OnWakeUp` — per-character dim/restore

**Rendering:** URP `DecalProjector` with shared materials (2 total: ally + enemy). No per-instance material clones. Per-instance opacity via `DecalProjector.fadeFactor`. Requires Decal Renderer Feature on URP Renderer assets.
```

- [ ] **Step 2: Commit**

```bash
git add .agent/skills/combat_system/SKILL.md
git commit -m "docs: update combat system skill with battle ground circle indicators"
```
