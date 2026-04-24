using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using MWI.Quests;

/// <summary>
/// Hybrid quest marker renderer:
///   • Every active quest gets a HUD marker (UI Image projected from the target's
///     world position via Camera.WorldToScreenPoint, edge-clamped to the screen border
///     when off-screen / behind the camera). The HUD marker is the always-visible
///     "where do I go" cue.
///   • Quests whose target reports a zone (IQuestTarget.GetZoneBounds() != null)
///     ALSO get a world-space zone-fill prefab spawned in the scene at the zone's
///     bounds — flat mesh on the ground, sized to the zone footprint. The HUD marker
///     anchors at the zone center; the world-space fill provides the "you're inside
///     the area" diegetic confirmation.
///   • Future polish (brainstorm option C — outlined target on close approach):
///     wire the legacy diamond / beacon prefabs to fade in when the player is within
///     ~5 units of the target.
///
/// Map-aware filter: a quest's marker(s) render only when
/// quest.OriginMapId == localPlayer.CharacterMapTracker.CurrentMapID.
///
/// One instance per local-player Character. PlayerUI.Initialize wires it up.
///
/// File / class name kept as 'QuestWorldMarkerRenderer' to preserve existing scene
/// + PlayerUI references. TODO: rename file + class to QuestMarkerRenderer in a
/// future cleanup pass (requires re-wiring the GameScene component reference).
/// </summary>
public class QuestWorldMarkerRenderer : MonoBehaviour
{
    [Header("HUD container — RectTransform under player HUD canvas (for screen-space markers)")]
    [Tooltip("Markers are instantiated as children of this RectTransform. Must live under a Canvas.")]
    [SerializeField] private RectTransform _markerContainer;
    [Tooltip("Optional. Falls back to Camera.main if null.")]
    [SerializeField] private Camera _gameplayCamera;

    [Header("HUD marker visuals (placeholder — designer iterates)")]
    [SerializeField] private Vector2 _markerSize = new Vector2(48f, 48f);
    [SerializeField] private Color _markerColor = new Color(1f, 0.83f, 0.29f, 1f);
    [Tooltip("Pixels of padding from each screen edge when edge-clamping off-screen markers.")]
    [SerializeField] private float _edgePadding = 50f;

    [Header("World-space prefabs")]
    [Tooltip("Spawned in world space at the zone's bounds when a quest target reports GetZoneBounds() != null.")]
    [SerializeField] private GameObject _zoneFillPrefab;

    [Header("Legacy world-space prefabs (unused — kept for future close-range polish)")]
    [Tooltip("Brainstorm option C: fade in the diamond when the player is within close-range of an object target.")]
    [SerializeField] private GameObject _diamondPrefab;
    [Tooltip("Brainstorm option B world-space variant: vertical light shaft for movement targets.")]
    [SerializeField] private GameObject _beaconPrefab;

    [Header("Diagnostics")]
    [Tooltip("Logs why each active quest is/isn't spawning a marker. Disable once stable.")]
    [SerializeField] private bool _verboseLogs = false;

    private CharacterQuestLog _log;
    private CharacterMapTracker _mapTracker;

    private class QuestVisuals
    {
        public RectTransform HudMarker;          // always non-null while the quest is on this map
        public GameObject WorldZoneInstance;     // non-null only when target.GetZoneBounds() != null
    }

    private readonly Dictionary<string, QuestVisuals> _visuals = new Dictionary<string, QuestVisuals>();
    private bool _syncRequested;

    public void Initialize(CharacterQuestLog log, CharacterMapTracker mapTracker)
    {
        if (_log != null)
        {
            _log.OnQuestAdded -= HandleQuestAdded;
            _log.OnQuestRemoved -= HandleQuestRemoved;
        }
        _log = log;
        if (_log != null)
        {
            _log.OnQuestAdded += HandleQuestAdded;
            _log.OnQuestRemoved += HandleQuestRemoved;
        }
        if (_mapTracker != null)
        {
            _mapTracker.CurrentMapID.OnValueChanged -= HandleMapChanged;
        }
        _mapTracker = mapTracker;
        if (_mapTracker != null)
        {
            _mapTracker.CurrentMapID.OnValueChanged += HandleMapChanged;
        }
        ClearAll();
    }

    private void HandleQuestAdded(IQuest quest) => _syncRequested = true;
    private void HandleQuestRemoved(IQuest quest) => _syncRequested = true;
    private void HandleMapChanged(FixedString128Bytes prev, FixedString128Bytes next) => ClearAll();

    private void Update()
    {
        if (_log == null)
        {
            if (_verboseLogs) Debug.LogWarning("[QuestMarker] Update bail: _log is null (Initialize was never called or got null).");
            return;
        }

        var cam = _gameplayCamera != null ? _gameplayCamera : Camera.main;
        if (cam == null)
        {
            if (_verboseLogs) Debug.LogWarning("[QuestMarker] Update bail: no camera (Camera.main and _gameplayCamera both null).");
            return;
        }

        string currentMapId = _mapTracker != null ? _mapTracker.CurrentMapID.Value.ToString() : string.Empty;

        // Diff active quests against currently-spawned visuals.
        var seen = new HashSet<string>();
        int activeCount = 0;
        foreach (var q in _log.ActiveQuests)
        {
            activeCount++;
            if (q == null || q.Target == null)
            {
                if (_verboseLogs) Debug.LogWarning($"[QuestMarker] Skip: quest {(q == null ? "null" : q.QuestId)} has null Target.");
                continue;
            }
            if (!string.IsNullOrEmpty(currentMapId) && !string.IsNullOrEmpty(q.OriginMapId) && q.OriginMapId != currentMapId)
            {
                if (_verboseLogs) Debug.Log($"[QuestMarker] Skip '{q.Title}': map mismatch (quest='{q.OriginMapId}', player='{currentMapId}').");
                continue;
            }

            seen.Add(q.QuestId);

            if (!_visuals.TryGetValue(q.QuestId, out var v) || v == null)
            {
                v = SpawnVisuals(q);
                _visuals[q.QuestId] = v;
            }

            if (v.HudMarker != null)
            {
                UpdateHudMarkerPosition(v.HudMarker, q.Target.GetWorldPosition(), cam);
            }
            // World-space zone-fill is static once spawned (anchored at zone bounds);
            // no per-frame work needed unless the zone moves at runtime.
        }

        if (_verboseLogs && activeCount == 0 && _visuals.Count == 0)
        {
            // Throttled to once per second so we don't flood Console.
            if (Time.frameCount % 60 == 0)
                Debug.Log($"[QuestMarker] No active quests in log. _log.ActiveQuests is empty.");
        }

        // Despawn visuals whose quests are no longer active.
        if (_syncRequested || _visuals.Count != seen.Count)
        {
            var toRemove = new List<string>();
            foreach (var kv in _visuals)
            {
                if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);
            }
            foreach (var id in toRemove)
            {
                if (_visuals.TryGetValue(id, out var v) && v != null) DestroyVisuals(v);
                _visuals.Remove(id);
            }
            _syncRequested = false;
        }
    }

    private QuestVisuals SpawnVisuals(IQuest quest)
    {
        var v = new QuestVisuals();
        var target = quest.Target;

        // Always spawn a HUD marker (if the container is wired).
        if (_markerContainer != null)
        {
            string label = quest.QuestId.Length >= 8 ? quest.QuestId.Substring(0, 8) : quest.QuestId;
            var go = new GameObject($"QuestMarker_{label}", typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_markerContainer, false);
            // Anchor + pivot at center so transform.position (set in pixel coords for
            // Screen Space Overlay canvases) places the marker's center at that pixel.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = _markerSize;
            rect.localScale = Vector3.one;
            var img = go.GetComponent<Image>();
            img.color = _markerColor;
            img.raycastTarget = false;
            v.HudMarker = rect;
            if (_verboseLogs) Debug.Log($"[QuestMarker] Spawned HUD marker for '{quest.Title}' under {_markerContainer.name}.");
        }
        else
        {
            Debug.LogWarning("[QuestMarker] _markerContainer is null — HUD markers will not render.");
        }

        // Spawn world-space zone fill if the target reports a zone.
        var bounds = target.GetZoneBounds();
        if (bounds.HasValue && _zoneFillPrefab != null)
        {
            var fill = Instantiate(_zoneFillPrefab, bounds.Value.center, Quaternion.identity);
            fill.transform.localScale = new Vector3(bounds.Value.size.x, 0.1f, bounds.Value.size.z);
            v.WorldZoneInstance = fill;
        }

        return v;
    }

    private void UpdateHudMarkerPosition(RectTransform rect, Vector3 worldPos, Camera cam)
    {
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

        // If the target is behind the camera, mirror its screen position to the opposite
        // side so the edge-clamp pins the marker to the correct screen border.
        if (screenPos.z < 0f)
        {
            screenPos.x = Screen.width - screenPos.x;
            screenPos.y = Screen.height - screenPos.y;
        }

        float clampedX = Mathf.Clamp(screenPos.x, _edgePadding, Screen.width - _edgePadding);
        float clampedY = Mathf.Clamp(screenPos.y, _edgePadding, Screen.height - _edgePadding);

        // Use ScreenPointToLocalPointInRectangle to convert screen pixels into the
        // marker container's local coordinate space. This handles all three Canvas modes
        // (Screen Space Overlay / Screen Space Camera / World Space) AND all CanvasScaler
        // settings correctly — using rect.anchoredPosition with raw screen pixels
        // breaks under "Scale With Screen Size" (where 1 unit != 1 pixel).
        var canvas = _markerContainer != null ? _markerContainer.GetComponentInParent<Canvas>() : null;
        Camera uiCam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : (canvas != null ? canvas.worldCamera : null);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerContainer, new Vector2(clampedX, clampedY), uiCam, out var localPoint))
        {
            rect.anchoredPosition = localPoint;
        }
    }

    private void DestroyVisuals(QuestVisuals v)
    {
        if (v.HudMarker != null) Destroy(v.HudMarker.gameObject);
        if (v.WorldZoneInstance != null) Destroy(v.WorldZoneInstance);
    }

    private void ClearAll()
    {
        foreach (var v in _visuals.Values) if (v != null) DestroyVisuals(v);
        _visuals.Clear();
    }

    private void OnDestroy()
    {
        ClearAll();
        if (_log != null)
        {
            _log.OnQuestAdded -= HandleQuestAdded;
            _log.OnQuestRemoved -= HandleQuestRemoved;
        }
        if (_mapTracker != null)
        {
            _mapTracker.CurrentMapID.OnValueChanged -= HandleMapChanged;
        }
    }
}
