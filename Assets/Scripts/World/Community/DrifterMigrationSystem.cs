using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using MWI.WorldSystem;

/// <summary>
/// Server-only daily migration ticker. Subscribes to <see cref="MWI.Time.TimeManager.OnNewDay"/>.
/// On each tick, for every chartered <see cref="Community"/> whose
/// <see cref="Community.AdministrativeBuilding"/> sits on this controller's map, instantiates
/// up to <see cref="_maxDriftersPerCommunityPerDay"/> drifter NPCs at random map-edge points
/// (NavMesh-sampled) and routes each to walk toward the AB's JoinRequestDesk (Task 6).
///
/// v1 spawn requires <see cref="_drifterPrefab"/> to be wired in the Inspector. If unset, the
/// system logs the intent but skips the spawn so the rest of the pipeline can still proceed
/// (the chartered city just doesn't gain drifters until designers wire a prefab).
///
/// Plan 4c Task 5.
/// </summary>
[RequireComponent(typeof(MapController))]
public class DrifterMigrationSystem : MonoBehaviour
{
    [Header("Migration tuning")]
    [Tooltip("Max drifter spawns per chartered community per OnNewDay tick.")]
    [SerializeField] private int _maxDriftersPerCommunityPerDay = 1;

    [Tooltip("Inward padding from the map edge when picking spawn points (world units).")]
    [SerializeField] private float _spawnEdgePadding = 5f;

    [Tooltip("Designer-wired generic NPC prefab. Until set, the system logs the intent and skips the spawn.")]
    [SerializeField] private GameObject _drifterPrefab;

    private MapController _map;
    private bool _subscribed;

    private void Awake()
    {
        _map = GetComponent<MapController>();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        // Server-only — clients run no migration logic. Defensive guard for late host-spawn
        // (NetworkManager may not be ready yet at OnEnable; we re-check inside HandleNewDay).
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (_subscribed && MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
            _subscribed = false;
        }
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;
        if (MWI.Time.TimeManager.Instance == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        _subscribed = true;
    }

    private void Update()
    {
        // Late-subscribe pattern — NetworkManager / TimeManager may spin up after this
        // component's OnEnable. Cheap retry until subscribed.
        if (!_subscribed) TrySubscribe();
    }

    private void HandleNewDay()
    {
        if (_map == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (CommunityManager.Instance == null) return;

        // Walk all active communities; filter to those that are chartered AND whose AB
        // resolves to THIS map.
        var communities = CommunityManager.Instance.activeCommunities;
        if (communities == null) return;

        for (int i = 0; i < communities.Count; i++)
        {
            var community = communities[i];
            if (community == null) continue;
            if (community.AdministrativeBuilding == null) continue;
            var ab = community.AdministrativeBuilding;
            if (ab.IsUnderConstruction) continue;

            var hostMap = MapController.GetMapAtPosition(ab.transform.position);
            if (hostMap != _map) continue;

            for (int n = 0; n < _maxDriftersPerCommunityPerDay; n++)
                TrySpawnOneDrifter(community);
        }
    }

    private void TrySpawnOneDrifter(Community community)
    {
        Vector3 spawn = PickRandomMapEdgePoint();
        if (!NavMesh.SamplePosition(spawn, out var hit, 10f, NavMesh.AllAreas))
        {
            Debug.LogWarning($"<color=orange>[DrifterMigration]</color> {community.communityName}: random edge point at {spawn} had no NavMesh within 10u; skipping spawn this tick.");
            return;
        }

        if (_drifterPrefab == null)
        {
            // v1 stub: log intent but skip spawn until designers wire the prefab.
            Debug.Log($"<color=#88aaff>[DrifterMigration]</color> {community.communityName}: would spawn 1 drifter at {hit.position} (no _drifterPrefab wired on {gameObject.name}).");
            return;
        }

        var go = Instantiate(_drifterPrefab, hit.position, Quaternion.identity);
        var character = go != null ? go.GetComponent<Character>() : null;
        if (character == null)
        {
            Debug.LogError($"<color=red>[DrifterMigration]</color> {community.communityName}: _drifterPrefab spawn yielded no Character component. Destroying.");
            if (go != null) Destroy(go);
            return;
        }

        var netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"<color=red>[DrifterMigration]</color> {community.communityName}: _drifterPrefab missing NetworkObject. Destroying.");
            Destroy(go);
            return;
        }
        netObj.Spawn();

        // Movement intent: walk toward the AB's interaction zone. Task 6's JoinRequestDesk
        // will provide a typed accessor; for now we route to the AB's transform.position so
        // the drifter at least walks into the city. Once Task 6 wires
        // ab.GetJoinRequestDesk(), the drifter's BT can OnInteract the desk on arrival.
        var movement = character.CharacterMovement;
        if (movement != null)
        {
            movement.SetDestination(community.AdministrativeBuilding.transform.position);
        }

        Debug.Log($"<color=green>[DrifterMigration]</color> {community.communityName}: spawned drifter '{character.CharacterName}' at {hit.position}; walking toward AB.");
    }

    private Vector3 PickRandomMapEdgePoint()
    {
        if (_map == null) return Vector3.zero;
        var box = _map.GetComponent<BoxCollider>();
        if (box == null) return _map.transform.position;

        var b = box.bounds;
        float pad = Mathf.Max(0f, _spawnEdgePadding);
        // Pick one of 4 edges (N/S/E/W), random T along the edge, then pull inward by pad.
        int edge = Random.Range(0, 4);
        float t = Random.value;
        Vector3 p;
        switch (edge)
        {
            case 0: // North edge (+Z)
                p = new Vector3(Mathf.Lerp(b.min.x + pad, b.max.x - pad, t), b.center.y, b.max.z - pad);
                break;
            case 1: // South edge (-Z)
                p = new Vector3(Mathf.Lerp(b.min.x + pad, b.max.x - pad, t), b.center.y, b.min.z + pad);
                break;
            case 2: // East edge (+X)
                p = new Vector3(b.max.x - pad, b.center.y, Mathf.Lerp(b.min.z + pad, b.max.z - pad, t));
                break;
            default: // West edge (-X)
                p = new Vector3(b.min.x + pad, b.center.y, Mathf.Lerp(b.min.z + pad, b.max.z - pad, t));
                break;
        }
        return p;
    }
}
