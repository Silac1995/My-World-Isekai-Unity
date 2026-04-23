using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using MWI.Quests;

/// <summary>
/// Spawns / despawns world-space marker prefabs for active quests on the local player.
/// Map-aware filtering: a quest's markers render only when
/// quest.OriginMapId == localPlayer.CharacterMapTracker.CurrentMapID.
///
/// One CharacterQuestLog per local-player Character drives this renderer.
/// PlayerUI.Initialize wires it up with the local player's references.
/// </summary>
public class QuestWorldMarkerRenderer : MonoBehaviour
{
    [Tooltip("Floating diamond prefab — used for object/action targets (no movement target, no zone).")]
    [SerializeField] private GameObject _diamondPrefab;
    [Tooltip("Vertical light shaft + ring decal — used when IQuestTarget.GetMovementTarget() != null.")]
    [SerializeField] private GameObject _beaconPrefab;
    [Tooltip("Flat zone-fill mesh sized to GetZoneBounds() — used when GetZoneBounds() != null.")]
    [SerializeField] private GameObject _zoneFillPrefab;

    private CharacterQuestLog _log;
    private CharacterMapTracker _mapTracker;
    private readonly Dictionary<string, List<GameObject>> _spawnedMarkers = new Dictionary<string, List<GameObject>>();

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
        RefreshAll();
    }

    private void HandleQuestAdded(IQuest quest) => RefreshAll();
    private void HandleQuestRemoved(IQuest quest) => RefreshAll();
    private void HandleMapChanged(FixedString128Bytes prev, FixedString128Bytes next) => RefreshAll();

    private void RefreshAll()
    {
        ClearAll();
        if (_log == null) return;
        string currentMapId = _mapTracker != null ? _mapTracker.CurrentMapID.Value.ToString() : string.Empty;

        foreach (var q in _log.ActiveQuests)
        {
            if (q == null || q.Target == null) continue;
            // If a current map is known, filter; if not, render everything (defensive — shouldn't happen post-spawn).
            if (!string.IsNullOrEmpty(currentMapId) && !string.IsNullOrEmpty(q.OriginMapId) && q.OriginMapId != currentMapId) continue;
            SpawnMarkersFor(q);
        }
    }

    private void SpawnMarkersFor(IQuest quest)
    {
        var spawned = new List<GameObject>();
        var t = quest.Target;

        var zoneBounds = t.GetZoneBounds();
        if (zoneBounds.HasValue && _zoneFillPrefab != null)
        {
            var fill = Instantiate(_zoneFillPrefab, zoneBounds.Value.center, Quaternion.identity);
            // Scale a flat quad to the zone footprint. Y kept thin so it sits on the ground.
            fill.transform.localScale = new Vector3(zoneBounds.Value.size.x, 0.1f, zoneBounds.Value.size.z);
            spawned.Add(fill);
        }

        var moveTarget = t.GetMovementTarget();
        if (moveTarget.HasValue && _beaconPrefab != null)
        {
            var beacon = Instantiate(_beaconPrefab, moveTarget.Value, Quaternion.identity);
            spawned.Add(beacon);
        }
        else if (!zoneBounds.HasValue && _diamondPrefab != null)
        {
            // Object/action target — diamond above the world position.
            var pos = t.GetWorldPosition() + Vector3.up * 2f;
            var diamond = Instantiate(_diamondPrefab, pos, Quaternion.identity);
            spawned.Add(diamond);
        }

        if (spawned.Count > 0) _spawnedMarkers[quest.QuestId] = spawned;
    }

    private void ClearAll()
    {
        foreach (var list in _spawnedMarkers.Values)
        {
            foreach (var go in list) if (go != null) Destroy(go);
        }
        _spawnedMarkers.Clear();
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
