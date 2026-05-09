# Loading Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a generic loading overlay (`LoadingOverlay` singleton + `NetworkConnectionLoadingDriver`) that surfaces NGO client-join progress as a stage-based progress bar with descriptive text, plus a cancel button that appears after 10 s.

**Architecture:** Push API + driver pattern. `LoadingOverlay` is a pure UI controller with `Show / SetStage / SetDetail / SetCancelHandler / ShowFailure / Hide` methods. `NetworkConnectionLoadingDriver` is a short-lived observer that subscribes to NGO events and translates them into overlay calls. Visual prefab is duplicated from `UI_PauseMenu.prefab` for style consistency. Spec: [docs/superpowers/specs/2026-04-25-loading-overlay-design.md](../specs/2026-04-25-loading-overlay-design.md).

**Tech Stack:** Unity 6000.x, Unity.Netcode for GameObjects 2.x, TextMeshPro, Unity UI (uGUI Slider/Image/Button), MCP Unity tools for prefab creation.

---

## File Structure

| File | Purpose |
|---|---|
| `Assets/Scripts/UI/Loading/LoadingOverlay.cs` | Lazy singleton MonoBehaviour. Public push API. Knows nothing about NGO. ~150 LOC. |
| `Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs` | Short-lived observer of NGO events. Pushes stage updates into `LoadingOverlay`. Self-destructs on connect/disconnect/cancel. ~150 LOC. |
| `Assets/Resources/UI/UI_LoadingOverlay.prefab` | Visual prefab. Copy of `UI_PauseMenu.prefab` style with progress-bar content. Lazy-loaded by `LoadingOverlay` via `Resources.Load`. |
| `Assets/Scripts/Core/Network/GameSessionManager.cs` | Modify `JoinMultiplayer()`: instantiate driver + call `LoadingOverlay.Show` before `StartClient`. ~3 lines added. |
| `wiki/systems/network.md` | Add a section documenting the loading-UI integration. |
| `.agent/skills/multiplayer/SKILL.md` | Add a short note on the driver pattern. |

---

### Task 1: Create LoadingOverlay singleton (UI controller, no NGO dependency)

**Files:**
- Create: `Assets/Scripts/UI/Loading/LoadingOverlay.cs`

- [ ] **Step 1: Write the LoadingOverlay class**

Create file `Assets/Scripts/UI/Loading/LoadingOverlay.cs` using the Write tool with this exact content:

```csharp
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Loading
{
    /// <summary>
    /// Generic full-screen loading overlay. Lazy singleton, DontDestroyOnLoad, client-side UI only.
    ///
    /// Pure UI controller — exposes a push API (Show / SetStage / SetDetail / SetCancelHandler /
    /// ShowFailure / Hide) and knows nothing about NGO, save/load, or scene transitions. Producers
    /// (drivers) observe their domain events and push stage updates here. The first ever call to
    /// <see cref="Show"/> instantiates <c>Resources/UI/UI_LoadingOverlay</c> and persists it across
    /// scene loads.
    ///
    /// All animations use <c>Time.unscaledDeltaTime</c> per project rule #26 — UI must remain
    /// responsive when <c>GameSpeedController</c> pauses or warps simulation time.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        private const string ResourcePath = "UI/UI_LoadingOverlay";

        [Header("Wired in prefab")]
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _stageText;
        [SerializeField] private TextMeshProUGUI _detailText;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private TextMeshProUGUI _cancelButtonLabel;
        [SerializeField] private CanvasGroup _cancelButtonCanvasGroup;

        [Header("Tuning")]
        [Tooltip("Seconds for the progress bar to tween to its target value.")]
        [SerializeField] private float _barTweenDuration = 0.25f;
        [Tooltip("Seconds for the cancel button fade-in once its delay elapses.")]
        [SerializeField] private float _cancelButtonFadeDuration = 0.4f;

        private static LoadingOverlay s_instance;

        private float _targetProgress01;
        private float _displayedProgress01;
        private float _shownAtUnscaledTime;
        private float _cancelButtonDelaySeconds;
        private bool _cancelButtonShown;
        private Action _onCancel;
        private Coroutine _tweenCoroutine;
        private Coroutine _cancelFadeCoroutine;

        public static LoadingOverlay Instance
        {
            get
            {
                if (s_instance != null) return s_instance;

                var prefab = Resources.Load<GameObject>(ResourcePath);
                if (prefab == null)
                {
                    Debug.LogError($"[LoadingOverlay] Prefab not found at Resources/{ResourcePath}.prefab. Cannot show overlay.");
                    return null;
                }

                var go = Instantiate(prefab);
                go.name = "UI_LoadingOverlay (singleton)";
                DontDestroyOnLoad(go);
                s_instance = go.GetComponent<LoadingOverlay>();
                if (s_instance == null)
                {
                    Debug.LogError($"[LoadingOverlay] Prefab at Resources/{ResourcePath} is missing the LoadingOverlay component on the root.");
                }
                return s_instance;
            }
        }

        public bool IsVisible => _panelRoot != null && _panelRoot.activeSelf;

        private void Awake()
        {
            // Self-register so direct scene placements also act as the singleton if present.
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;

            if (_panelRoot != null) _panelRoot.SetActive(false);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(HandleCancelClicked);
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
        }

        public void Show(string title)
        {
            EnsurePanelActive();
            if (_titleText != null) _titleText.text = title ?? string.Empty;
            if (_stageText != null) _stageText.text = string.Empty;
            if (_detailText != null) _detailText.text = string.Empty;

            _targetProgress01 = 0f;
            _displayedProgress01 = 0f;
            if (_progressBar != null) _progressBar.value = 0f;

            _shownAtUnscaledTime = Time.unscaledTime;
            _cancelButtonShown = false;
            _onCancel = null;
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
            if (_cancelButtonLabel != null) _cancelButtonLabel.text = "Cancel";

            CancelTweens();
        }

        public void SetStage(string stageText, float progress01)
        {
            EnsurePanelActive();
            if (_stageText != null) _stageText.text = stageText ?? string.Empty;
            _targetProgress01 = Mathf.Clamp01(progress01);
            StartBarTween();
        }

        public void SetDetail(string detail)
        {
            if (_detailText != null) _detailText.text = detail ?? string.Empty;
        }

        public void SetCancelHandler(Action onCancel, float cancelDelaySeconds = 10f)
        {
            _onCancel = onCancel;
            _cancelButtonDelaySeconds = Mathf.Max(0f, cancelDelaySeconds);
            // Reset shown flag — Update() will re-decide based on elapsed time.
            _cancelButtonShown = false;
            if (_cancelFadeCoroutine != null) { StopCoroutine(_cancelFadeCoroutine); _cancelFadeCoroutine = null; }
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
        }

        public void ShowFailure(string reason)
        {
            EnsurePanelActive();
            if (_stageText != null) _stageText.text = string.IsNullOrEmpty(reason) ? "Connection failed." : $"Connection failed: {reason}";
            if (_detailText != null) _detailText.text = string.Empty;
            _targetProgress01 = 1f;
            StartBarTween();

            // Repurpose the cancel button as "Back to main menu" — surface immediately.
            _cancelButtonShown = true;
            if (_cancelButtonLabel != null) _cancelButtonLabel.text = "Back to main menu";
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 1f;
            if (_cancelButton != null) _cancelButton.interactable = true;
        }

        public void Hide()
        {
            CancelTweens();
            if (_panelRoot != null) _panelRoot.SetActive(false);
            _onCancel = null;
            if (_cancelButtonCanvasGroup != null) _cancelButtonCanvasGroup.alpha = 0f;
            if (_cancelButton != null) _cancelButton.interactable = false;
        }

        private void Update()
        {
            if (!IsVisible) return;

            // Cancel-button delay countdown (unscaled — survives GameSpeedController pause).
            if (!_cancelButtonShown && _onCancel != null)
            {
                if (Time.unscaledTime - _shownAtUnscaledTime >= _cancelButtonDelaySeconds)
                {
                    _cancelButtonShown = true;
                    if (_cancelFadeCoroutine != null) StopCoroutine(_cancelFadeCoroutine);
                    _cancelFadeCoroutine = StartCoroutine(FadeInCancelButton());
                }
            }
        }

        private IEnumerator FadeInCancelButton()
        {
            if (_cancelButton != null) _cancelButton.interactable = true;
            if (_cancelButtonCanvasGroup == null) yield break;

            float t = 0f;
            float start = _cancelButtonCanvasGroup.alpha;
            while (t < _cancelButtonFadeDuration)
            {
                t += Time.unscaledDeltaTime;
                _cancelButtonCanvasGroup.alpha = Mathf.Lerp(start, 1f, Mathf.Clamp01(t / _cancelButtonFadeDuration));
                yield return null;
            }
            _cancelButtonCanvasGroup.alpha = 1f;
        }

        private void StartBarTween()
        {
            if (_tweenCoroutine != null) StopCoroutine(_tweenCoroutine);
            _tweenCoroutine = StartCoroutine(BarTween());
        }

        private IEnumerator BarTween()
        {
            float start = _displayedProgress01;
            float end = _targetProgress01;
            float t = 0f;
            float duration = Mathf.Max(0.01f, _barTweenDuration);
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _displayedProgress01 = Mathf.Lerp(start, end, Mathf.Clamp01(t / duration));
                if (_progressBar != null) _progressBar.value = _displayedProgress01;
                yield return null;
            }
            _displayedProgress01 = end;
            if (_progressBar != null) _progressBar.value = _displayedProgress01;
        }

        private void CancelTweens()
        {
            if (_tweenCoroutine != null) { StopCoroutine(_tweenCoroutine); _tweenCoroutine = null; }
            if (_cancelFadeCoroutine != null) { StopCoroutine(_cancelFadeCoroutine); _cancelFadeCoroutine = null; }
        }

        private void EnsurePanelActive()
        {
            if (_panelRoot != null && !_panelRoot.activeSelf) _panelRoot.SetActive(true);
        }

        private void HandleCancelClicked()
        {
            var cb = _onCancel;
            // Clear so the same click can't double-fire if a coroutine reads it later.
            _onCancel = null;
            try { cb?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (_cancelButton != null) _cancelButton.onClick.RemoveListener(HandleCancelClicked);
            CancelTweens();
        }
    }
}
```

- [ ] **Step 2: Compile-check**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (logTypeFilter=Error, lastMinutes=1)
```
Expected: no compile errors (the persistent MCP path warning is fine).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/Loading/LoadingOverlay.cs Assets/Scripts/UI/Loading/LoadingOverlay.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui): add LoadingOverlay singleton skeleton (UI controller)

Lazy DontDestroyOnLoad singleton with push API (Show / SetStage /
SetDetail / SetCancelHandler / ShowFailure / Hide). Knows nothing
about NGO — pure UI controller. All animations use unscaled time
per rule #26. Prefab wiring in next task.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Create UI_LoadingOverlay.prefab via MCP

**Files:**
- Create: `Assets/Resources/UI/UI_LoadingOverlay.prefab`

The prefab visually mirrors `UI_PauseMenu.prefab` (full-screen dim background + centered panel) but contains a progress-bar layout instead of menu buttons.

- [ ] **Step 1: Ensure the Resources/UI folder exists**

Run via MCP:
```
mcp__ai-game-developer__assets-create-folder (path="Assets/Resources/UI")
```
Expected: success (or "already exists" — both fine).

- [ ] **Step 2: Build the prefab via script-execute**

Use a single MCP `script-execute` call so the prefab is built atomically with all serialized references wired. Run via `mcp__ai-game-developer__script-execute` with `className="BuildLoadingOverlayPrefab"` `methodName="Run"` and this code:

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public class BuildLoadingOverlayPrefab
{
    public static string Run()
    {
        // Root canvas
        var root = new GameObject("UI_LoadingOverlay");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // above gameplay HUD
        root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // Panel root (this is what gets SetActive(true/false))
        var panelGO = new GameObject("PanelRoot");
        panelGO.transform.SetParent(root.transform, false);
        var panelRect = panelGO.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Background dim
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(panelGO.transform, false);
        var bgRect = bgGO.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.85f);
        bgImg.raycastTarget = true; // blocks underlying clicks

        // Center panel
        var centerGO = new GameObject("CenterPanel");
        centerGO.transform.SetParent(panelGO.transform, false);
        var centerRect = centerGO.AddComponent<RectTransform>();
        centerRect.anchorMin = new Vector2(0.5f, 0.5f);
        centerRect.anchorMax = new Vector2(0.5f, 0.5f);
        centerRect.pivot = new Vector2(0.5f, 0.5f);
        centerRect.sizeDelta = new Vector2(800, 320);
        centerRect.anchoredPosition = Vector2.zero;
        var centerImg = centerGO.AddComponent<Image>();
        centerImg.color = new Color(0.10f, 0.10f, 0.12f, 0.95f);

        // Title text (top of center panel)
        var titleGO = new GameObject("TitleText");
        titleGO.transform.SetParent(centerGO.transform, false);
        var titleRect = titleGO.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0, -20);
        titleRect.sizeDelta = new Vector2(-40, 60);
        var titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
        titleTmp.text = "Loading…";
        titleTmp.fontSize = 36;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.white;

        // Stage text (middle)
        var stageGO = new GameObject("StageText");
        stageGO.transform.SetParent(centerGO.transform, false);
        var stageRect = stageGO.AddComponent<RectTransform>();
        stageRect.anchorMin = new Vector2(0f, 0.5f);
        stageRect.anchorMax = new Vector2(1f, 0.5f);
        stageRect.pivot = new Vector2(0.5f, 0.5f);
        stageRect.anchoredPosition = new Vector2(0, 30);
        stageRect.sizeDelta = new Vector2(-40, 40);
        var stageTmp = stageGO.AddComponent<TextMeshProUGUI>();
        stageTmp.text = "";
        stageTmp.fontSize = 22;
        stageTmp.alignment = TextAlignmentOptions.Center;
        stageTmp.color = new Color(0.9f, 0.9f, 0.9f, 1f);

        // Detail text (small, below stage)
        var detailGO = new GameObject("DetailText");
        detailGO.transform.SetParent(centerGO.transform, false);
        var detailRect = detailGO.AddComponent<RectTransform>();
        detailRect.anchorMin = new Vector2(0f, 0.5f);
        detailRect.anchorMax = new Vector2(1f, 0.5f);
        detailRect.pivot = new Vector2(0.5f, 0.5f);
        detailRect.anchoredPosition = new Vector2(0, -5);
        detailRect.sizeDelta = new Vector2(-40, 28);
        var detailTmp = detailGO.AddComponent<TextMeshProUGUI>();
        detailTmp.text = "";
        detailTmp.fontSize = 16;
        detailTmp.alignment = TextAlignmentOptions.Center;
        detailTmp.color = new Color(0.7f, 0.7f, 0.7f, 1f);

        // Progress bar (Slider, near bottom of center panel)
        var sliderGO = new GameObject("ProgressBar");
        sliderGO.transform.SetParent(centerGO.transform, false);
        var sliderRect = sliderGO.AddComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0f, 0f);
        sliderRect.anchorMax = new Vector2(1f, 0f);
        sliderRect.pivot = new Vector2(0.5f, 0f);
        sliderRect.anchoredPosition = new Vector2(0, 80);
        sliderRect.sizeDelta = new Vector2(-60, 20);
        var slider = sliderGO.AddComponent<Slider>();
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;

        // Slider background
        var sliderBgGO = new GameObject("Background");
        sliderBgGO.transform.SetParent(sliderGO.transform, false);
        var sliderBgRect = sliderBgGO.AddComponent<RectTransform>();
        sliderBgRect.anchorMin = Vector2.zero;
        sliderBgRect.anchorMax = Vector2.one;
        sliderBgRect.offsetMin = Vector2.zero;
        sliderBgRect.offsetMax = Vector2.zero;
        var sliderBgImg = sliderBgGO.AddComponent<Image>();
        sliderBgImg.color = new Color(0.15f, 0.15f, 0.18f, 1f);

        // Slider fill area
        var fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        var fillAreaRect = fillAreaGO.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(2, 2);
        fillAreaRect.offsetMax = new Vector2(-2, -2);

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        var fillRect = fillGO.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.30f, 0.65f, 0.95f, 1f);

        slider.fillRect = fillRect;
        slider.targetGraphic = sliderBgImg;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 0f;
        slider.direction = Slider.Direction.LeftToRight;

        // Cancel button (bottom of center panel)
        var cancelGO = new GameObject("CancelButton");
        cancelGO.transform.SetParent(centerGO.transform, false);
        var cancelRect = cancelGO.AddComponent<RectTransform>();
        cancelRect.anchorMin = new Vector2(0.5f, 0f);
        cancelRect.anchorMax = new Vector2(0.5f, 0f);
        cancelRect.pivot = new Vector2(0.5f, 0f);
        cancelRect.anchoredPosition = new Vector2(0, 20);
        cancelRect.sizeDelta = new Vector2(220, 44);
        var cancelImg = cancelGO.AddComponent<Image>();
        cancelImg.color = new Color(0.20f, 0.20f, 0.24f, 1f);
        var cancelButton = cancelGO.AddComponent<Button>();
        cancelButton.targetGraphic = cancelImg;
        var cancelCG = cancelGO.AddComponent<CanvasGroup>();
        cancelCG.alpha = 0f;

        var cancelLabelGO = new GameObject("Label");
        cancelLabelGO.transform.SetParent(cancelGO.transform, false);
        var cancelLabelRect = cancelLabelGO.AddComponent<RectTransform>();
        cancelLabelRect.anchorMin = Vector2.zero;
        cancelLabelRect.anchorMax = Vector2.one;
        cancelLabelRect.offsetMin = Vector2.zero;
        cancelLabelRect.offsetMax = Vector2.zero;
        var cancelLabelTmp = cancelLabelGO.AddComponent<TextMeshProUGUI>();
        cancelLabelTmp.text = "Cancel";
        cancelLabelTmp.fontSize = 18;
        cancelLabelTmp.alignment = TextAlignmentOptions.Center;
        cancelLabelTmp.color = Color.white;

        // Add the LoadingOverlay component on the root and wire serialized references via reflection.
        var overlayType = System.Type.GetType("MWI.UI.Loading.LoadingOverlay, Assembly-CSharp");
        if (overlayType == null) return "ERROR: MWI.UI.Loading.LoadingOverlay type not found. Did Task 1 compile?";
        var overlayComp = root.AddComponent(overlayType) as MonoBehaviour;
        if (overlayComp == null) return "ERROR: failed to add LoadingOverlay component.";

        var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        overlayType.GetField("_panelRoot", bf).SetValue(overlayComp, panelGO);
        overlayType.GetField("_titleText", bf).SetValue(overlayComp, titleTmp);
        overlayType.GetField("_stageText", bf).SetValue(overlayComp, stageTmp);
        overlayType.GetField("_detailText", bf).SetValue(overlayComp, detailTmp);
        overlayType.GetField("_progressBar", bf).SetValue(overlayComp, slider);
        overlayType.GetField("_cancelButton", bf).SetValue(overlayComp, cancelButton);
        overlayType.GetField("_cancelButtonLabel", bf).SetValue(overlayComp, cancelLabelTmp);
        overlayType.GetField("_cancelButtonCanvasGroup", bf).SetValue(overlayComp, cancelCG);

        // Save as prefab.
        const string outputPath = "Assets/Resources/UI/UI_LoadingOverlay.prefab";
        bool success;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, outputPath, out success);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return success
            ? $"OK: prefab saved at {outputPath} (guid {AssetDatabase.AssetPathToGUID(outputPath)})."
            : $"ERROR: SaveAsPrefabAsset returned false for {outputPath}.";
    }
}
```

Expected return value: `OK: prefab saved at Assets/Resources/UI/UI_LoadingOverlay.prefab (guid …).`

If the script reports `MWI.UI.Loading.LoadingOverlay type not found`, the assembly hasn't recompiled. Run `mcp__ai-game-developer__assets-refresh` first and retry.

- [ ] **Step 3: Verify the prefab via script-execute**

Run via `mcp__ai-game-developer__script-execute` with `className="VerifyLoadingOverlayPrefab"` `methodName="Run"`:

```csharp
using UnityEngine;
using UnityEditor;

public class VerifyLoadingOverlayPrefab
{
    public static string Run()
    {
        var prefab = Resources.Load<GameObject>("UI/UI_LoadingOverlay");
        if (prefab == null) return "ERROR: Resources.Load returned null for UI/UI_LoadingOverlay.";

        var overlayType = System.Type.GetType("MWI.UI.Loading.LoadingOverlay, Assembly-CSharp");
        var comp = prefab.GetComponent(overlayType);
        if (comp == null) return "ERROR: prefab missing LoadingOverlay component.";

        var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        string[] fields = { "_panelRoot", "_titleText", "_stageText", "_detailText", "_progressBar", "_cancelButton", "_cancelButtonLabel", "_cancelButtonCanvasGroup" };
        foreach (var f in fields)
        {
            var v = overlayType.GetField(f, bf).GetValue(comp);
            if (v == null || (v is Object u && u == null)) return $"ERROR: serialized field {f} is null.";
        }
        return "OK: all 8 serialized references wired.";
    }
}
```

Expected: `OK: all 8 serialized references wired.`

- [ ] **Step 4: Commit**

```bash
git add Assets/Resources Assets/Resources.meta
git commit -m "$(cat <<'EOF'
feat(ui): add UI_LoadingOverlay prefab (Resources/UI/)

Full-screen dim + centered panel matching UI_PauseMenu's style.
Contains: title, stage text, detail text, progress slider, cancel
button (initially CanvasGroup alpha 0). All 8 serialized refs on
the LoadingOverlay component are wired via the build script.

Lives in Resources/ so the singleton can lazy-load without scene
authoring.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Smoke-test LoadingOverlay in editor playmode

This validates Show / SetStage / SetDetail / SetCancelHandler / ShowFailure / Hide all work end-to-end on the actual prefab before we layer the NGO driver on top.

- [ ] **Step 1: Enter playmode and exercise the API via script-execute**

Run via `mcp__ai-game-developer__editor-application-set-state` to enter playmode:
```
state="playmode"
```

Wait for playmode to start, then run via `mcp__ai-game-developer__script-execute` with `className="SmokeTestLoadingOverlay"` `methodName="Run"`:

```csharp
using System.Collections;
using UnityEngine;
using MWI.UI.Loading;

public class SmokeTestLoadingOverlay
{
    public static string Run()
    {
        var instance = LoadingOverlay.Instance;
        if (instance == null) return "ERROR: LoadingOverlay.Instance returned null (prefab missing or component not on root).";

        instance.Show("Smoke test");
        instance.SetStage("Stage A — should fill 25%", 0.25f);
        instance.SetDetail("(detail line)");
        instance.SetCancelHandler(() => Debug.Log("[SmokeTest] Cancel clicked."), cancelDelaySeconds: 2f);

        return "OK: Show + SetStage + SetDetail + SetCancelHandler invoked. Visually confirm: panel visible, bar at ~25% after tween, cancel button fades in after 2s.";
    }
}
```

Expected: the OK message AND a visible loading overlay in the Game view with bar at ~25%, "Stage A — should fill 25%" text, and (after 2 s) a cancel button fading in.

- [ ] **Step 2: Trigger the failure-state and Hide paths**

Run via `mcp__ai-game-developer__script-execute` with `className="SmokeTestLoadingOverlayPart2"` `methodName="Run"`:

```csharp
using UnityEngine;
using MWI.UI.Loading;

public class SmokeTestLoadingOverlayPart2
{
    public static string Run()
    {
        var i = LoadingOverlay.Instance;
        if (i == null) return "ERROR: instance lost.";
        i.ShowFailure("Lost connection to host (test)");
        return "OK: failure state set. Visually confirm: cancel button label now reads 'Back to main menu', visible immediately.";
    }
}
```

Expected: panel still visible, stage text reads "Connection failed: Lost connection to host (test)", cancel button shows "Back to main menu" and is immediately interactive.

Then run `Hide`:

```csharp
using MWI.UI.Loading;

public class SmokeTestLoadingOverlayHide
{
    public static string Run()
    {
        LoadingOverlay.Instance?.Hide();
        return "OK: Hide() called.";
    }
}
```

Expected: panel disappears.

- [ ] **Step 3: Exit playmode**

Run via `mcp__ai-game-developer__editor-application-set-state`:
```
state="edit"
```

If anything in steps 1–2 failed visually, fix the prefab/script before proceeding (do NOT proceed to Task 4 with a broken overlay).

- [ ] **Step 4: No commit**

This is a verification task — no files change.

---

### Task 4: Create NetworkConnectionLoadingDriver

**Files:**
- Create: `Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs`

- [ ] **Step 1: Write the driver**

Create file `Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs` using the Write tool with this exact content:

```csharp
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MWI.UI.Loading
{
    /// <summary>
    /// Short-lived MonoBehaviour that observes Netcode-for-GameObjects connection events
    /// and pushes stage updates into <see cref="LoadingOverlay"/>. Created by
    /// <c>GameSessionManager.JoinMultiplayer()</c> immediately before <c>StartClient()</c>;
    /// self-destructs on connect, disconnect, or cancel.
    ///
    /// Stage map (matches docs/superpowers/specs/2026-04-25-loading-overlay-design.md §5):
    ///   1. OnClientStarted              → "Connecting to host…"        0.10
    ///   2. (after StartClient returned) → "Awaiting host approval…"    0.25
    ///   3. SceneEvent.Load              → "Loading scene: {name}…"     0.40
    ///   4. SceneEvent.Synchronize       → "Synchronizing world…"       0.60→0.90 (asymptotic)
    ///   5. SceneEvent.SynchronizeComplete → "Finalizing…"              0.95
    ///   6. OnClientConnectedCallback    → Hide() + self-destruct
    ///   7. OnClientDisconnectCallback (pre-connect) → ShowFailure
    ///
    /// Stage 4 polls <c>NetworkManager.SpawnManager.SpawnedObjectsList.Count</c> at 10 Hz
    /// (unscaled time) and pushes <c>SetDetail("{n} entities loaded")</c>. The bar fill
    /// follows <c>0.60 + 0.30 * n / (n + 50)</c>.
    /// </summary>
    public class NetworkConnectionLoadingDriver : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenuScene";
        private const float SpawnPollIntervalSeconds = 0.1f;

        private bool _connected;
        private bool _inSynchronizeStage;
        private int _spawnBaseline;
        private Coroutine _spawnPollCoroutine;

        private NetworkManager _nm;

        private void OnEnable()
        {
            _nm = NetworkManager.Singleton;
            if (_nm == null)
            {
                Debug.LogError("[NetworkConnectionLoadingDriver] NetworkManager.Singleton is null on enable. Driver will self-destruct.");
                Destroy(gameObject);
                return;
            }

            _nm.OnClientStarted += HandleClientStarted;
            _nm.OnClientConnectedCallback += HandleClientConnected;
            _nm.OnClientDisconnectCallback += HandleClientDisconnect;

            if (_nm.SceneManager != null)
            {
                _nm.SceneManager.OnSceneEvent += HandleSceneEvent;
            }
            else
            {
                // SceneManager is created when StartClient succeeds. Watch for it via a one-shot poll.
                StartCoroutine(WatchForSceneManager());
            }
        }

        private void OnDisable()
        {
            if (_nm != null)
            {
                _nm.OnClientStarted -= HandleClientStarted;
                _nm.OnClientConnectedCallback -= HandleClientConnected;
                _nm.OnClientDisconnectCallback -= HandleClientDisconnect;
                if (_nm.SceneManager != null) _nm.SceneManager.OnSceneEvent -= HandleSceneEvent;
            }
            if (_spawnPollCoroutine != null) { StopCoroutine(_spawnPollCoroutine); _spawnPollCoroutine = null; }
        }

        private IEnumerator WatchForSceneManager()
        {
            // Poll once per frame until SceneManager exists or we self-destruct.
            while (this != null && _nm != null && _nm.SceneManager == null)
            {
                yield return null;
            }
            if (this != null && _nm != null && _nm.SceneManager != null)
            {
                _nm.SceneManager.OnSceneEvent += HandleSceneEvent;
            }
        }

        private void HandleClientStarted()
        {
            var overlay = LoadingOverlay.Instance;
            if (overlay == null) return;
            overlay.SetStage("Connecting to host…", 0.10f);
            // Stage 2 — set the "awaiting approval" text right after the underlying transport handshake.
            // No explicit NGO event for this transition, so we time-fade after one frame.
            StartCoroutine(StepToAwaitingApproval());
        }

        private IEnumerator StepToAwaitingApproval()
        {
            yield return null; // one frame later
            if (_connected) yield break;
            LoadingOverlay.Instance?.SetStage("Awaiting host approval…", 0.25f);
        }

        private void HandleSceneEvent(SceneEvent ev)
        {
            // Only react to events targeted at the local client.
            // The synchronize sequence is fired against the connecting client's id.
            var overlay = LoadingOverlay.Instance;
            if (overlay == null || _connected) return;

            switch (ev.SceneEventType)
            {
                case SceneEventType.Load:
                    overlay.SetStage($"Loading scene: {ev.SceneName}…", 0.40f);
                    break;

                case SceneEventType.Synchronize:
                    EnterSynchronizeStage();
                    break;

                case SceneEventType.SynchronizeComplete:
                    ExitSynchronizeStage();
                    overlay.SetStage("Finalizing…", 0.95f);
                    break;
            }
        }

        private void EnterSynchronizeStage()
        {
            _inSynchronizeStage = true;
            _spawnBaseline = _nm != null && _nm.SpawnManager != null
                ? _nm.SpawnManager.SpawnedObjectsList.Count
                : 0;

            var overlay = LoadingOverlay.Instance;
            if (overlay != null)
            {
                overlay.SetStage("Synchronizing world…", 0.60f);
                overlay.SetDetail("0 entities loaded");
            }

            if (_spawnPollCoroutine != null) StopCoroutine(_spawnPollCoroutine);
            _spawnPollCoroutine = StartCoroutine(PollSpawnCount());
        }

        private void ExitSynchronizeStage()
        {
            _inSynchronizeStage = false;
            if (_spawnPollCoroutine != null) { StopCoroutine(_spawnPollCoroutine); _spawnPollCoroutine = null; }
            LoadingOverlay.Instance?.SetDetail(string.Empty);
        }

        private IEnumerator PollSpawnCount()
        {
            var wait = new WaitForSecondsRealtime(SpawnPollIntervalSeconds);
            while (_inSynchronizeStage && this != null && _nm != null && _nm.SpawnManager != null)
            {
                int count = _nm.SpawnManager.SpawnedObjectsList.Count - _spawnBaseline;
                if (count < 0) count = 0;

                float fill = 0.60f + 0.30f * (count / (count + 50f));
                if (fill > 0.90f) fill = 0.90f;

                var overlay = LoadingOverlay.Instance;
                if (overlay != null)
                {
                    overlay.SetStage("Synchronizing world…", fill);
                    overlay.SetDetail($"{count} entities loaded");
                }
                yield return wait;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (_nm == null || clientId != _nm.LocalClientId) return;
            _connected = true;
            LoadingOverlay.Instance?.Hide();
            Destroy(gameObject);
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            // Only react to OUR client id disconnecting (or the unknown-pre-connect 0).
            if (_nm != null && clientId != _nm.LocalClientId && _connected) return;
            if (_connected)
            {
                // Disconnect AFTER a successful connect — overlay was already hidden, do nothing.
                Destroy(gameObject);
                return;
            }

            string reason = _nm != null && !string.IsNullOrEmpty(_nm.DisconnectReason)
                ? _nm.DisconnectReason
                : "lost connection to host";

            var overlay = LoadingOverlay.Instance;
            if (overlay != null)
            {
                overlay.SetCancelHandler(BackToMainMenu, cancelDelaySeconds: 0f);
                overlay.ShowFailure(reason);
            }
            // Do NOT self-destruct yet — let the user click "Back to main menu" to leave.
        }

        public void RegisterCancelHandler()
        {
            // Called by GameSessionManager.JoinMultiplayer right after instantiating us.
            LoadingOverlay.Instance?.SetCancelHandler(CancelJoin, cancelDelaySeconds: 10f);
        }

        private void CancelJoin()
        {
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
            BackToMainMenu();
        }

        private void BackToMainMenu()
        {
            LoadingOverlay.Instance?.Hide();
            Destroy(gameObject);
            SceneManager.LoadScene(MainMenuSceneName);
        }
    }
}
```

- [ ] **Step 2: Compile-check**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (logTypeFilter=Error, lastMinutes=1)
```
Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs Assets/Scripts/UI/Loading/NetworkConnectionLoadingDriver.cs.meta
git commit -m "$(cat <<'EOF'
feat(ui): add NetworkConnectionLoadingDriver

Short-lived observer that translates NGO connection lifecycle events
(OnClientStarted, OnSceneEvent Load/Synchronize/SynchronizeComplete,
OnClientConnectedCallback, OnClientDisconnectCallback) into stage
updates pushed to LoadingOverlay. Polls SpawnManager.SpawnedObjectsList.Count
at 10 Hz during the synchronize stage and renders the count via
SetDetail. Cancel handler shuts the network down and loads the main
menu scene.

Wiring into GameSessionManager.JoinMultiplayer in next task.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Wire driver into GameSessionManager.JoinMultiplayer

**Files:**
- Modify: `Assets/Scripts/Core/Network/GameSessionManager.cs` around line 472 (`JoinMultiplayer`)

- [ ] **Step 1: Read the current JoinMultiplayer method**

Use the Read tool on `Assets/Scripts/Core/Network/GameSessionManager.cs` covering lines 460–510 to confirm the exact current shape of `JoinMultiplayer()` and find the right insertion points.

- [ ] **Step 2: Insert the loading-overlay + driver wiring**

Use the Edit tool to add the loading-overlay setup just before `NetworkManager.Singleton.StartClient()`. The exact edit:

`old_string` should be the block that contains `if (NetworkManager.Singleton.StartClient())` (read it fresh in Step 1 — do not paste from this plan, since whitespace and nearby lines may differ).

`new_string` should be the same block prefixed with these three lines (matching the surrounding indentation):

```csharp
            // Show the loading overlay and spin up the driver BEFORE StartClient — the driver
            // hooks NetworkManager.OnClientStarted in its OnEnable, so it must exist before the
            // event fires. The driver self-destructs on connect/disconnect/cancel.
            MWI.UI.Loading.LoadingOverlay.Instance?.Show("Joining game…");
            var loadingDriverGo = new GameObject("NetworkConnectionLoadingDriver");
            var loadingDriver = loadingDriverGo.AddComponent<MWI.UI.Loading.NetworkConnectionLoadingDriver>();
            loadingDriver.RegisterCancelHandler();

```

- [ ] **Step 3: Compile-check**

Run via MCP:
```
mcp__ai-game-developer__assets-refresh
mcp__ai-game-developer__console-get-logs (logTypeFilter=Error, lastMinutes=1)
```
Expected: no compile errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/Network/GameSessionManager.cs
git commit -m "$(cat <<'EOF'
feat(net): show LoadingOverlay during multiplayer client join

JoinMultiplayer now spawns NetworkConnectionLoadingDriver and shows
the LoadingOverlay before StartClient(). Driver hooks NGO connection
events and self-destructs on connect/disconnect/cancel.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: End-to-end multiplayer test

This is a manual playtest. The plan's job is to enumerate exactly what to look for so anyone running the test can validate the feature works.

- [ ] **Step 1: Build a standalone test client**

Use Unity's File → Build And Run (or `mcp__ai-game-developer__script-execute` to invoke `BuildPipeline.BuildPlayer(...)` if a CI build is preferred). Output: a standalone executable that can connect to a separately-running editor host.

- [ ] **Step 2: Happy path — fresh world, immediate join**

1. Start the Unity editor in playmode as host (existing flow).
2. Place 1 building (Forge) so we exercise the synchronize stage with at least one network-spawned NO.
3. Launch the standalone client and connect.
4. Watch the standalone client's screen.

Expected sequence on the client (each visible briefly):
- "Joining game…" title appears.
- Stage text cycles: "Connecting to host…" (bar 10%) → "Awaiting host approval…" (25%) → "Loading scene: …" (40%) → "Synchronizing world…" (60→~70% with "N entities loaded" subline) → "Finalizing…" (95%).
- Overlay disappears within ~2 s of total connection time.
- Cancel button NEVER appears (delay is 10 s, total join is faster).

If any stage is skipped visually, the corresponding NGO event isn't firing — investigate that event in the driver, do not patch by guessing.

- [ ] **Step 3: Stuck-join path — kill host mid-sync**

1. Repeat steps 1–3 of Step 2.
2. While the standalone client is still in stage 4 ("Synchronizing world…"), kill the host editor process (close window or stop playmode).

Expected on the client:
- After 10 s of unscaled time (cancel-button delay): a "Cancel" button fades in at the bottom of the panel.
- Eventually NGO's `SpawnTimeout` (30 s) fires and `OnClientDisconnectCallback` triggers `ShowFailure`.
- Stage text changes to "Connection failed: <reason>".
- The cancel button label changes to "Back to main menu" and remains visible.
- Click it → main menu scene loads.

- [ ] **Step 4: User-cancel path**

1. Launch a heavy world that takes longer than 10 s to sync (load a saved world with many NPCs/buildings).
2. Connect from the standalone client.
3. Wait 10 s. Cancel button appears.
4. Click Cancel.

Expected:
- `NetworkManager.Shutdown()` runs cleanly (no errors in the client console).
- Main menu scene loads.
- No leaked `NetworkConnectionLoadingDriver` GameObjects (verify via Unity's Hierarchy in the standalone, if a development build).

- [ ] **Step 5: Repeat-join leak check**

1. Connect, succeed, return to menu.
2. Connect again, succeed, return to menu.
3. Repeat 5 times.

Expected: each connection shows the overlay fresh; no duplicate `UI_LoadingOverlay (singleton)` GameObjects appear; no leaked drivers.

- [ ] **Step 6: No commit**

Manual test only. If anything failed, return to the relevant earlier task and fix.

---

### Task 7: Update docs

**Files:**
- Modify: `wiki/systems/network.md` (add a "Connection loading UI" section + bump frontmatter `updated:` and append a change log entry)
- Modify: `.agent/skills/multiplayer/SKILL.md` (add a short note about the driver pattern)

- [ ] **Step 1: Update wiki/systems/network.md frontmatter**

Use the Edit tool on `wiki/systems/network.md` to bump `updated:` to `2026-04-25`.

`old_string`:
```
created: 2026-04-19
updated: 2026-04-25
```

(If the date is already `2026-04-25` from the prior session, leave it alone. Read the file first to confirm.)

- [ ] **Step 2: Append a Connection loading UI section to wiki/systems/network.md**

Use the Edit tool. `old_string`: the existing `## Specialized agents` heading line (read fresh, exact whitespace matters).

`new_string`: a new section inserted BEFORE `## Specialized agents`:

```markdown
## Connection loading UI

A remote client joining a host can take several seconds while NGO walks the scene-sync handshake. `LoadingOverlay` (singleton, `DontDestroyOnLoad`, lazy-loads `Resources/UI/UI_LoadingOverlay.prefab`) renders a stage-based progress bar with descriptive text. `NetworkConnectionLoadingDriver` is a short-lived observer instantiated by `GameSessionManager.JoinMultiplayer()` immediately before `StartClient()`; it subscribes to NGO connection lifecycle events (`OnClientStarted`, `OnSceneEvent` of types `Load` / `Synchronize` / `SynchronizeComplete`, `OnClientConnectedCallback`, `OnClientDisconnectCallback`) and pushes stage updates into the overlay.

Stages: `Connecting → Awaiting approval → Loading scene → Synchronizing world (with N-entities-loaded counter) → Finalizing → Hide`. Stage 4 polls `NetworkManager.SpawnManager.SpawnedObjectsList.Count` at 10 Hz of unscaled time; the bar fill follows `0.60 + 0.30 * n / (n + 50)` so it advances visibly without ever reaching the next stage. A cancel button stays hidden for the first 10 s and fades in afterwards — short joins never see it; stalled joins get an escape hatch well before NGO's `SpawnTimeout` (30 s) fires.

The overlay's API (`Show / SetStage / SetDetail / SetCancelHandler / ShowFailure / Hide`) is generic — additional drivers (save-load, scene transitions, solo session boot) can drive the same overlay without changes. See `docs/superpowers/specs/2026-04-25-loading-overlay-design.md` for the full design.

## Specialized agents
```

(The `## Specialized agents` line is duplicated at the end of `new_string` so the Edit tool keeps it after the inserted section.)

- [ ] **Step 3: Append change-log entry to wiki/systems/network.md**

Use the Edit tool. `old_string`: the first existing `## Change log` line plus the next bullet (read fresh).

`new_string`: same `## Change log` heading, then a new bullet inserted as the most-recent entry:

```markdown
## Change log
- 2026-04-25 — Added connection loading UI: `LoadingOverlay` singleton + `NetworkConnectionLoadingDriver` observer. Generic overlay (Resources/UI/UI_LoadingOverlay.prefab) reusable for any future loading scenario; driver translates NGO lifecycle events into stage-based progress (Connecting → Awaiting approval → Loading scene → Synchronizing → Finalizing) with a 10-s-delayed cancel button and a failure state. Spec: docs/superpowers/specs/2026-04-25-loading-overlay-design.md. — claude
```

(Keep all existing change-log entries below this new line.)

- [ ] **Step 4: Add a note to .agent/skills/multiplayer/SKILL.md**

Use the Edit tool to append a new bullet to the §10 list (after the "InvalidParentException" rule). `old_string`: the line that begins `- **NGO `InvalidParentException` — never SetParent…` plus everything up to and including its trailing period (read fresh).

`new_string`: the same content followed by:

```markdown
- **Surface NGO connection progress to UI via the driver pattern, never by polling from a UI MonoBehaviour.** UI components should never subscribe to `NetworkManager` events directly — it couples UI to networking and leaks event handlers across scene loads. Instead: a short-lived **driver** observes the relevant NGO events and pushes already-translated stage data into a generic UI controller (e.g. `MWI.UI.Loading.LoadingOverlay`). `NetworkConnectionLoadingDriver` is the canonical example for client-join progress; future loading scenarios (save-load, scene transitions, solo session boot) follow the same shape — implement a new driver, reuse the overlay.
```

- [ ] **Step 5: Commit**

```bash
git add wiki/systems/network.md .agent/skills/multiplayer/SKILL.md
git commit -m "$(cat <<'EOF'
docs: surface LoadingOverlay + NetworkConnectionLoadingDriver

- wiki/systems/network.md: new "Connection loading UI" section
  documenting the overlay + driver pattern, frontmatter bumped,
  change log entry appended.
- multiplayer/SKILL.md §10: add the driver-pattern rule for
  surfacing NGO progress to UI without coupling.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review (run after writing the plan)

**Spec coverage check** (each spec section → task that implements it):

| Spec § | Task |
|---|---|
| §3 Architecture (LoadingOverlay + Driver + prefab) | Tasks 1, 2, 4 |
| §4 LoadingOverlay API | Task 1 |
| §5 Driver lifetime + stage map | Tasks 4, 5 |
| §5 Spawn counter + 0.60+0.30·n/(n+50) formula | Task 4 |
| §5 Cancel button 10 s delay | Tasks 1, 4 |
| §6 Failure paths | Tasks 1, 4 |
| §7 Integration in JoinMultiplayer | Task 5 |
| §8 File layout | Tasks 1, 2, 4 |
| §9 Edge cases (idempotency, scene survival, leak prevention) | Tasks 1, 4, 6 |
| §10 Testing | Task 6 |
| §11 Documentation | Task 7 |

No gaps.

**Placeholder scan**: no TBD/TODO/"add error handling"/"similar to Task N". All code blocks are complete and copy-pasteable.

**Type/name consistency**: all serialized field names (`_panelRoot`, `_titleText`, `_stageText`, `_detailText`, `_progressBar`, `_cancelButton`, `_cancelButtonLabel`, `_cancelButtonCanvasGroup`) match between Task 1 (class definition), Task 2 (prefab build script reflection), and Task 3 (verification script). Public method names (`Show`, `SetStage`, `SetDetail`, `SetCancelHandler`, `ShowFailure`, `Hide`) match between Task 1 and the smoke test. Driver public method `RegisterCancelHandler()` defined in Task 4 and called in Task 5.
