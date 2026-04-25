using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional renderer for <see cref="StorageFurniture"/>. Add this component to a
/// shelf-style prefab to visually display the stored items at authored anchor
/// positions; omit it for chests / barrels where contents shouldn't be visible.
///
/// **Visual pipeline mirrors <see cref="WorldItem.Initialize"/> directly** but
/// instantiates <see cref="ItemSO.ItemPrefab"/> (the visual sub-prefab — same
/// content `WorldItem.AttachVisualPrefab` uses internally) instead of the full
/// <c>WorldItemPrefab</c> wrapper. Avoiding the wrapper means no cloned
/// `NetworkObject` can interfere with parenting/visibility on clients, where NGO
/// is more sensitive to "homeless" NetworkObjects than on the host. We add a
/// `SortingGroup` to the spawned root so 2D sprites layer correctly — that's the
/// only thing the wrapper gave us beyond raw visuals.
///
/// TODO (deferred — 2026-04-25): per-player local distance/visibility gating.
/// Distance-gating was removed because it was a global host-side decision and
/// hid items from approaching clients. The replacement should be a per-player
/// local culler that runs on each peer independently, so each client only pays
/// the spawn/render cost when ITS local player is in range. Hooks into the
/// existing local-player resolution and runs decoupled from the server's
/// inventory sync. Until then, displays are always-on whenever the storage
/// contents include items.
///
/// Performance design:
/// <list type="bullet">
///   <item>Per-ItemSO object pool — taking and re-storing the same item type
///         doesn't allocate after the first time. Pool key is the SO reference,
///         so different `ItemSO`s never share a pooled instance.</item>
///   <item>Event-driven: subscribes to <see cref="StorageFurniture.OnInventoryChanged"/>,
///         no per-frame work in steady state.</item>
///   <item>Anchors are filled by the FIRST non-empty slots in slot order
///         (misc → weapon → wearable → any), so 5 anchors over a 32-capacity
///         storage simply show the first 5 items regardless of slot index.</item>
/// </list>
/// Networking: this component reacts to <see cref="StorageFurniture.OnInventoryChanged"/>
/// regardless of authority. As long as the storage data is synced to clients (handled
/// by the network sync layer), each peer's display rebuilds locally on their own
/// frame — no replication of the visual GameObjects themselves.
/// </summary>
[RequireComponent(typeof(StorageFurniture))]
public class StorageVisualDisplay : MonoBehaviour
{
    [Header("Display Setup")]
    [SerializeField] private StorageFurniture _storage;
    [Tooltip("Anchor transforms. Anchors are filled by the FIRST non-empty slots iterated in slot order " +
             "(misc → weapon → wearable → any), independent of slot index. So 5 anchors on a shelf with 8 misc + 8 wearable + ... " +
             "will display the first 5 stored items regardless of which slot type they live in. " +
             "Add more anchors than the storage capacity for no extra cost — extras stay unused.")]
    [SerializeField] private List<Transform> _displayAnchors = new List<Transform>();

    [Header("Visual Tuning")]
    [Tooltip("Uniform scale applied to every spawned item visual. Items are typically authored at full world size; " +
             "shelves usually want them shrunk. 0.5–1.0 is a good range.")]
    [SerializeField, Range(0.05f, 4f)] private float _itemScale = 0.7f;

    // Active displays paired with the ItemSO they render — we need the SO key on
    // return so the pool routes the right way.
    private readonly List<(GameObject go, ItemSO so)> _activeDisplays = new List<(GameObject, ItemSO)>();

    // Per-ItemSO object pool. Different SOs never share a slot — an apple display
    // can never be reused for a sword.
    private readonly Dictionary<ItemSO, Stack<GameObject>> _pool = new Dictionary<ItemSO, Stack<GameObject>>();

    private void Reset()
    {
        _storage = GetComponent<StorageFurniture>();
    }

    private void Awake()
    {
        if (_storage == null) _storage = GetComponent<StorageFurniture>();
    }

    private void OnEnable()
    {
        if (_storage != null)
        {
            _storage.OnInventoryChanged += Rebuild;
        }
        else
        {
            Debug.LogWarning($"<color=orange>[StorageVisualDisplay]</color> {name}: _storage is NULL on OnEnable — no subscription, visual will never update. Wire the StorageFurniture reference in the Inspector or rely on the GetComponent fallback in Awake.");
        }

        // Initial pass — render whatever is already in the storage at load time.
        Rebuild();
    }

    private void OnDisable()
    {
        if (_storage != null) _storage.OnInventoryChanged -= Rebuild;
        ReturnAllToPool();
    }

    private void OnDestroy()
    {
        if (_storage != null) _storage.OnInventoryChanged -= Rebuild;
    }

    private void Rebuild()
    {
        ReturnAllToPool();
        if (_storage == null || _displayAnchors == null || _displayAnchors.Count == 0) return;

        // Walk every slot in declaration order and consume anchors as we find non-empty
        // slots. See class docstring for why we don't use a direct slot[i] ↔ anchor[i] mapping.
        int slotCount = _storage.Capacity;
        int anchorIndex = 0;
        for (int slotIndex = 0; slotIndex < slotCount && anchorIndex < _displayAnchors.Count; slotIndex++)
        {
            var slot = _storage.GetItemSlot(slotIndex);
            if (slot == null || slot.IsEmpty()) continue;

            var item = slot.ItemInstance;
            if (item == null || item.ItemSO == null) continue;

            var anchor = _displayAnchors[anchorIndex];
            anchorIndex++;
            if (anchor == null) continue;

            var display = AcquireDisplay(item, anchor);
            if (display == null) continue;

            _activeDisplays.Add((display, item.ItemSO));
        }
    }

    /// <summary>
    /// Pops a pooled display for this ItemSO if any, else instantiates a fresh
    /// <see cref="ItemSO.ItemPrefab"/> clone — the visual sub-prefab, NOT the full
    /// <c>WorldItemPrefab</c> wrapper. We avoid the wrapper so cloned `NetworkObject`s
    /// can't interfere with parenting/visibility on clients, where NGO is more
    /// sensitive to "homeless" NetworkObjects than the host. Replicates the wearable-
    /// handler + color-injection logic that <see cref="WorldItem.Initialize"/> runs
    /// internally, plus a `SortingGroup` wrapper so 2D sprites layer correctly the
    /// same way they do on dropped items.
    /// </summary>
    private GameObject AcquireDisplay(ItemInstance item, Transform anchor)
    {
        var so = item.ItemSO;
        GameObject go = null;

        if (_pool.TryGetValue(so, out var stack))
        {
            while (stack.Count > 0)
            {
                go = stack.Pop();
                if (go != null) break; // skip stale references from scene reloads
                go = null;
            }
        }

        bool firstSpawn = false;
        if (go == null)
        {
            if (so.ItemPrefab == null)
            {
                Debug.LogWarning($"<color=orange>[StorageVisualDisplay]</color> {so.ItemName} has no ItemPrefab — cannot render.");
                return null;
            }
            go = Instantiate(so.ItemPrefab);
            // Match the SortingGroup the WorldItemPrefab wrapper provides for in-world
            // drops — without it, 2D sprites can render in the wrong order or be hidden
            // by the shelf's own renderers.
            if (go.GetComponent<UnityEngine.Rendering.SortingGroup>() == null)
            {
                go.AddComponent<UnityEngine.Rendering.SortingGroup>();
            }
            StripRuntimeComponents(go);
            firstSpawn = true;
        }
        else
        {
            go.SetActive(true);
        }

        // Reparent + position now — no NetworkObject means SetParent sticks immediately.
        go.transform.SetParent(anchor, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * _itemScale;

        // Apply the visual the same way WorldItem.Initialize does on dropped items.
        ApplyItemVisual(go, item, firstSpawn);

        return go;
    }

    /// <summary>
    /// Mirrors the visual-config block of <see cref="WorldItem.Initialize"/>: prefer
    /// the wearable handler when present (sprite library + category + colors), else
    /// fall back to <see cref="ItemInstance.InitializeWorldPrefab"/> for simple items.
    /// Always re-applies <see cref="ItemSO.CastsShadow"/> so pooled re-acquires reflect
    /// the latest item config.
    /// </summary>
    private static void ApplyItemVisual(GameObject go, ItemInstance item, bool firstSpawn)
    {
        var so = item.ItemSO;

        var handler = go.GetComponentInChildren<WearableHandlerBase>(includeInactive: true);
        if (handler != null)
        {
            handler.Initialize(so.SpriteLibraryAsset);
            handler.SetLibraryCategory(so.CategoryName);
            if (item is EquipmentInstance eq)
            {
                if (eq.HavePrimaryColor()) handler.SetPrimaryColor(eq.PrimaryColor);
                if (eq.HaveSecondaryColor()) handler.SetSecondaryColor(eq.SecondaryColor);
                handler.SetMainColor(Color.white);
            }
        }
        else if (firstSpawn)
        {
            // Simple-item path (apple, potion). Same fallback the WorldItem path uses.
            // Only run on first spawn — pooled re-acquires for the same SO already had it.
            try { item.InitializeWorldPrefab(go); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[StorageVisualDisplay] InitializeWorldPrefab threw on {so.ItemName}.");
            }
        }

        var mode = so.CastsShadow
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++) renderers[i].shadowCastingMode = mode;
    }

    /// <summary>
    /// Items in storage are inert — disable colliders, physics, and navmesh so the
    /// shelf display can't push workers, fall, or carve the navmesh.
    /// </summary>
    private static void StripRuntimeComponents(GameObject go)
    {
        // Colliders → disable so the static shelf display can't trigger interactions.
        var colliders = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;

        // Rigidbodies → kinematic, no gravity. Items don't fall off shelves.
        var bodies = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].isKinematic = true;
            bodies[i].useGravity = false;
        }

        // NavMeshObstacles → disable so the shelf items don't carve the navmesh.
        var obstacles = go.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true);
        for (int i = 0; i < obstacles.Length; i++) obstacles[i].enabled = false;
    }

    private void ReturnAllToPool()
    {
        for (int i = 0; i < _activeDisplays.Count; i++)
        {
            var (go, so) = _activeDisplays[i];
            if (go == null) continue;
            go.SetActive(false);
            go.transform.SetParent(transform, worldPositionStays: false);

            if (!_pool.TryGetValue(so, out var stack))
            {
                stack = new Stack<GameObject>();
                _pool[so] = stack;
            }
            stack.Push(go);
        }
        _activeDisplays.Clear();
    }
}
