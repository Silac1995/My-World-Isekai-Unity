using System.Collections.Generic;
using UnityEngine;

public class CharacterAwareness : CharacterSystem
{
    [SerializeField] private CapsuleCollider _awarenessCollider;

    public event System.Action<Character> OnCharacterDetected;

    // --- Caching layer (perf, see wiki/projects/optimisation-backlog.md entry #2 / Aₐ).
    // GetVisibleInteractables runs at BT tick rate (~10 Hz) × multiple BT conditions
    // × every NPC. Pre-refactor: fresh List + Collider[] alloc per call + per-call
    // Debug.Log on the typed overload (host-progressive-freeze pattern). Now: shared
    // cached list + reused OverlapSphere buffer + 0.3 s TTL.
    // The returned list from the untyped overload is a SHARED reference — callers
    // MUST treat it as read-only and MUST NOT hold it across ticks.
    private const float CacheTTLSeconds = 0.3f;
    private const int OverlapBufferSize = 64;
    private readonly Collider[] _overlapBuffer = new Collider[OverlapBufferSize];
    private readonly List<InteractableObject> _cachedInteractables = new List<InteractableObject>(OverlapBufferSize);
    private float _cacheValidUntil = -1f;

    public float AwarenessRadius
    {
        get
        {
            if (_awarenessCollider == null) return 15f;
            return _awarenessCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        }
    }

    /// <summary>
    /// Returns interactables within the awareness radius. The returned list is a
    /// SHARED, REUSED cache reference — callers must treat it as read-only and must
    /// not hold onto it across ticks (it gets refilled in place on the next refresh).
    /// Cached for <see cref="CacheTTLSeconds"/> of simulation time. Uses
    /// <see cref="Physics.OverlapSphereNonAlloc"/> against a reused buffer.
    /// </summary>
    public List<InteractableObject> GetVisibleInteractables()
    {
        if (_awarenessCollider == null)
        {
            _cachedInteractables.Clear();
            return _cachedInteractables;
        }

        if (Time.time < _cacheValidUntil)
        {
            return _cachedInteractables;
        }

        float radius = AwarenessRadius;

        // OverlapSphereNonAlloc fills our reused buffer (zero alloc).
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            _overlapBuffer,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide);

        _cachedInteractables.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            var hit = _overlapBuffer[i];
            if (hit == null) continue;

            var interactable = hit.GetComponent<InteractableObject>() ?? hit.GetComponentInParent<InteractableObject>();
            if (interactable == null) continue;
            if (interactable.RootGameObject == _character.gameObject) continue;
            if (_cachedInteractables.Contains(interactable)) continue;

            // Ensures we only target the object if its physical body is truly within our zone.
            Vector3 referencePos = interactable.Rigidbody != null
                ? interactable.Rigidbody.position
                : interactable.transform.position;

            if (Vector3.Distance(transform.position, referencePos) <= radius)
            {
                _cachedInteractables.Add(interactable);
            }
        }

        _cacheValidUntil = Time.time + CacheTTLSeconds;
        return _cachedInteractables;
    }

    /// <summary>
    /// Typed overload. Returns a fresh <see cref="List{T}"/> each call so callers
    /// may mutate freely. The OverlapSphere itself is still cached via
    /// <see cref="GetVisibleInteractables"/>; only the typed filter runs per call.
    /// </summary>
    public List<T> GetVisibleInteractables<T>() where T : InteractableObject
    {
        var source = GetVisibleInteractables();
        var filtered = new List<T>();
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] is T typed) filtered.Add(typed);
        }
        return filtered;
    }

    /// <summary>
    /// Force the next <see cref="GetVisibleInteractables"/> call to refresh, bypassing
    /// the TTL. Use after the world changed in a way that should be reflected immediately
    /// (e.g., a tracked target despawned, the player dropped an item the NPC was looking for).
    /// </summary>
    public void InvalidateCache()
    {
        _cacheValidUntil = -1f;
    }

    private void OnTriggerEnter(Collider other)
    {
        var interactable = other.GetComponent<CharacterInteractable>() ?? other.GetComponentInParent<CharacterInteractable>();

        if (interactable != null && interactable.Character != null && interactable.Character != _character)
        {
            // Make sure their physical Rigidbody actually entered the trigger.
            if (interactable.Rigidbody != null)
            {
                if (Vector3.Distance(transform.position, interactable.Rigidbody.position) > AwarenessRadius)
                    return; // Too far away

            }

            OnCharacterDetected?.Invoke(interactable.Character);
        }
    }
}
