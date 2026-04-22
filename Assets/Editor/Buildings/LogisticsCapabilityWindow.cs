#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only diagnostic window for the logistics capability graph of the
/// currently open scene(s). Helps designers spot "demanded but nobody
/// produces it" mismatches before runtime surfaces them as stalled BuyOrders.
///
/// Opened via: <c>MWI → Logistics → Capability Report</c>.
///
/// Scan sources:
/// - Demand: every <see cref="IStockProvider"/>.GetStockTargets() + each
///   active <see cref="CraftingOrder"/>'s recipe ingredients.
/// - Supply: every <see cref="CommercialBuilding"/>.ProducesItem(item) === true
///   for each demanded item (pulled this way because ProducesItem is a
///   query method, not an enumeration — the scene scan is editor-time so the
///   O(buildings × demanded items) cost is acceptable).
/// </summary>
public class LogisticsCapabilityWindow : EditorWindow
{
    private Vector2 _scroll;
    private bool _includeUndemanded = true;

    private List<Row> _unsuppliableRows = new List<Row>();
    private List<Row> _undemandedRows = new List<Row>();

    private struct Row
    {
        public ItemSO Item;
        public List<CommercialBuilding> Demanders;
        public List<CommercialBuilding> Suppliers;
    }

    [MenuItem("MWI/Logistics/Capability Report")]
    public static void Open()
    {
        var window = GetWindow<LogisticsCapabilityWindow>("Logistics Capability");
        window.minSize = new Vector2(520, 320);
        window.Scan();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rescan current scene(s)", GUILayout.Height(22)))
            {
                Scan();
            }
            _includeUndemanded = GUILayout.Toggle(_includeUndemanded, "Show supplied-but-undemanded", GUILayout.Width(240));
        }

        EditorGUILayout.HelpBox(
            Application.isPlaying
                ? "Running in Play Mode — scan reflects live scene state."
                : "Editor-time scan. Only buildings present in currently open scene(s) are counted. Procedural / hibernated buildings are ignored.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        DrawUnsuppliableSection();
        if (_includeUndemanded) DrawUndemandedSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawUnsuppliableSection()
    {
        var redBg = new GUIStyle(EditorStyles.helpBox);
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.55f, 0.55f, 1f);
        EditorGUILayout.BeginVertical(redBg);
        GUI.backgroundColor = prevBg;

        EditorGUILayout.LabelField($"Demanded but unsuppliable ({_unsuppliableRows.Count})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Items demanded by at least one building with no producer in the scene. These BuyOrders will stall at runtime.", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        if (_unsuppliableRows.Count == 0)
        {
            EditorGUILayout.LabelField("— none —", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var row in _unsuppliableRows)
            {
                DrawRow(row, showSuppliers: false);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawUndemandedSection()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"Supplied but undemanded ({_undemandedRows.Count})", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Items at least one building produces but no building demands (informational).", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        if (_undemandedRows.Count == 0)
        {
            EditorGUILayout.LabelField("— none —", EditorStyles.miniLabel);
            return;
        }

        var prevColor = GUI.color;
        GUI.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        foreach (var row in _undemandedRows)
        {
            DrawRow(row, showSuppliers: true);
        }
        GUI.color = prevColor;
    }

    private void DrawRow(Row row, bool showSuppliers)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            string itemName = row.Item != null ? row.Item.ItemName : "<null ItemSO>";
            EditorGUILayout.LabelField(itemName, EditorStyles.boldLabel);

            if (row.Demanders != null && row.Demanders.Count > 0)
            {
                EditorGUILayout.LabelField($"Demanders ({row.Demanders.Count}):", EditorStyles.miniLabel);
                foreach (var b in row.Demanders)
                {
                    if (b == null) continue;
                    if (GUILayout.Button($"  → {b.name} ({b.GetType().Name})", EditorStyles.linkLabel))
                    {
                        EditorGUIUtility.PingObject(b);
                        Selection.activeObject = b;
                    }
                }
            }

            if (showSuppliers && row.Suppliers != null && row.Suppliers.Count > 0)
            {
                EditorGUILayout.LabelField($"Suppliers ({row.Suppliers.Count}):", EditorStyles.miniLabel);
                foreach (var b in row.Suppliers)
                {
                    if (b == null) continue;
                    if (GUILayout.Button($"  → {b.name} ({b.GetType().Name})", EditorStyles.linkLabel))
                    {
                        EditorGUIUtility.PingObject(b);
                        Selection.activeObject = b;
                    }
                }
            }
        }
    }

    // =========================================================================
    // SCAN
    // =========================================================================

    private void Scan()
    {
        _unsuppliableRows.Clear();
        _undemandedRows.Clear();

        CommercialBuilding[] allBuildings;
        try
        {
#if UNITY_2023_1_OR_NEWER
            allBuildings = UnityEngine.Object.FindObjectsByType<CommercialBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            allBuildings = UnityEngine.Object.FindObjectsOfType<CommercialBuilding>(true);
#endif
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[LogisticsCapabilityWindow] Failed to enumerate CommercialBuildings in the current scene.");
            return;
        }

        if (allBuildings == null || allBuildings.Length == 0)
        {
            Repaint();
            return;
        }

        // 1. Build the demand map.
        var demand = new Dictionary<ItemSO, List<CommercialBuilding>>();

        foreach (var b in allBuildings)
        {
            if (b == null) continue;

            if (b is IStockProvider provider)
            {
                IEnumerable<StockTarget> targets = null;
                try { targets = provider.GetStockTargets(); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    Debug.LogError($"[LogisticsCapabilityWindow] {b.name}: GetStockTargets threw. Skipping this building's demand.");
                }

                if (targets != null)
                {
                    foreach (var t in targets)
                    {
                        if (t.ItemToStock == null) continue;
                        AddToMap(demand, t.ItemToStock, b);
                    }
                }
            }

            // Active crafting orders (only populated at runtime, but cheap to scan — safe editor-time).
            if (Application.isPlaying && b.LogisticsManager != null)
            {
                var craftOrders = b.LogisticsManager.ActiveCraftingOrders;
                if (craftOrders != null)
                {
                    foreach (var order in craftOrders)
                    {
                        if (order == null || order.ItemToCraft == null) continue;
                        var recipe = order.ItemToCraft.CraftingRecipe;
                        if (recipe == null) continue;
                        foreach (var ing in recipe)
                        {
                            if (ing.Item == null) continue;
                            AddToMap(demand, ing.Item, b);
                        }
                    }
                }
            }
        }

        // 2. Build the supply map.
        // ProducesItem() on CraftingBuilding indirects through Rooms → FurnitureManager.Furnitures,
        // which is only populated in Room.Awake() at runtime. In edit mode it's empty, so a
        // direct ProducesItem() call would false-negative. We precompute an edit-time-safe
        // craftable-set per CraftingBuilding by walking GetComponentsInChildren<CraftingStation>(true)
        // and reading _craftableItems straight from the serialized station data.
        var craftableByBuilding = new Dictionary<CraftingBuilding, HashSet<ItemSO>>();
        var itemsToProbe = new HashSet<ItemSO>(demand.Keys);

        foreach (var b in allBuildings)
        {
            if (!(b is CraftingBuilding cb)) continue;

            var crafts = new HashSet<ItemSO>();
            try
            {
                var stations = cb.GetComponentsInChildren<CraftingStation>(true);
                foreach (var station in stations)
                {
                    if (station == null || station.CraftableItems == null) continue;
                    foreach (var item in station.CraftableItems)
                    {
                        if (item != null) crafts.Add(item);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[LogisticsCapabilityWindow] {b.name}: edit-time crafting-station scan threw.");
            }

            craftableByBuilding[cb] = crafts;
            foreach (var item in crafts) itemsToProbe.Add(item);
        }

        var supply = new Dictionary<ItemSO, List<CommercialBuilding>>();
        foreach (var item in itemsToProbe)
        {
            foreach (var b in allBuildings)
            {
                if (b == null) continue;

                bool produces = false;

                // CraftingBuilding → use the edit-time-safe set we just computed.
                if (b is CraftingBuilding cb && craftableByBuilding.TryGetValue(cb, out var crafts))
                {
                    produces = crafts.Contains(item);
                }
                else
                {
                    // HarvestingBuilding / VirtualResourceSupplier don't need runtime state — safe to call.
                    try { produces = b.ProducesItem(item); }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        Debug.LogError($"[LogisticsCapabilityWindow] {b.name}: ProducesItem({item?.ItemName}) threw. Treating as false.");
                    }
                }

                if (produces) AddToMap(supply, item, b);
            }
        }

        // 3. Categorise.
        foreach (var kvp in demand)
        {
            if (!supply.TryGetValue(kvp.Key, out var suppliers) || suppliers.Count == 0)
            {
                _unsuppliableRows.Add(new Row
                {
                    Item = kvp.Key,
                    Demanders = kvp.Value,
                    Suppliers = null
                });
            }
        }

        foreach (var kvp in supply)
        {
            if (!demand.TryGetValue(kvp.Key, out var demanders) || demanders.Count == 0)
            {
                _undemandedRows.Add(new Row
                {
                    Item = kvp.Key,
                    Demanders = null,
                    Suppliers = kvp.Value
                });
            }
        }

        _unsuppliableRows = _unsuppliableRows.OrderBy(r => r.Item != null ? r.Item.ItemName : "").ToList();
        _undemandedRows = _undemandedRows.OrderBy(r => r.Item != null ? r.Item.ItemName : "").ToList();

        Repaint();
    }

    private static void AddToMap(Dictionary<ItemSO, List<CommercialBuilding>> map, ItemSO key, CommercialBuilding value)
    {
        if (key == null || value == null) return;
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<CommercialBuilding>();
            map[key] = list;
        }
        if (!list.Contains(value)) list.Add(value);
    }
}
#endif
