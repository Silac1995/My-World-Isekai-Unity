using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using MWI.Farming;
using MWI.Terrain;

/// <summary>
/// IInspectorView for any <see cref="Harvestable"/> target — covers wilderness harvestables
/// (trees, rocks, ore veins) and crop harvestables alike. A single view that handles every
/// concrete subclass via runtime polymorphism, mirroring <see cref="StorageFurnitureInspectorView"/>
/// and <see cref="BuildingInspectorView"/>. Refreshes every frame; cheap because
/// <see cref="DevInspectModule"/> keeps non-active views disabled.
///
/// Renders sections (top to bottom):
///   1. Identity — concrete type, name, layer, world position, NetworkObject id (if any).
///   2. Harvestable state — category, depleted flag, remaining yield, harvest duration,
///      required harvest tool, output items.
///   3. Destruction — only when <c>AllowDestruction</c> is true: required tool, outputs,
///      output count, duration.
///   4. Crop — only when the target is a <see cref="CropHarvestable"/>: cropId / display
///      name, growth stage, perennial / regrow days, cell coords, and full
///      <see cref="TerrainCell"/> readout (moisture / fertility / plowed flag /
///      growth timer / time-since-watered).
/// </summary>
public class HarvestableInspectorView : MonoBehaviour, IInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content;

    private Harvestable _target;

    public bool CanInspect(InteractableObject target)
    {
        return target is Harvestable;
    }

    public void SetTarget(InteractableObject target)
    {
        _target = target as Harvestable;
        UpdateHeader();
    }

    public void Clear()
    {
        _target = null;
        UpdateHeader();
        if (_content != null) _content.text = "<color=grey>No harvestable selected.</color>";
    }

    private void Update()
    {
        if (_target == null || _content == null) return;
        try
        {
            _content.text = RenderContent(_target);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            _content.text = $"<color=red>⚠ {GetType().Name} failed — {e.Message}</color>";
        }
    }

    private void UpdateHeader()
    {
        if (_headerLabel == null) return;
        if (_target == null)
        {
            _headerLabel.text = "Inspecting: —";
            return;
        }
        _headerLabel.text = $"Inspecting: {_target.gameObject.name}";
    }

    // ─── Rendering ────────────────────────────────────────────────────────

    private static string RenderContent(Harvestable h)
    {
        var sb = new StringBuilder(1024);

        AppendIdentity(sb, h);
        sb.AppendLine();
        AppendHarvestableState(sb, h);

        if (h.AllowDestruction)
        {
            sb.AppendLine();
            AppendDestruction(sb, h);
        }

        // Crop section: gated on the Harvestable carrying a CropSO data root (post-2026-04-29
        // unification — there is no `CropHarvestable` type any more, just Harvestable + a
        // CropSO `_so`).
        if (h.SO is MWI.Farming.CropSO)
        {
            sb.AppendLine();
            AppendCropSection(sb, h);
        }

        return sb.ToString();
    }

    private static void AppendIdentity(StringBuilder sb, Harvestable h)
    {
        sb.AppendLine("<b><color=#FFFFFF>Identity</color></b>");
        sb.Append("  Type: <color=#88CCFF>").Append(h.GetType().Name).AppendLine("</color>");
        sb.Append("  GameObject: ").AppendLine(h.gameObject.name);
        sb.Append("  Layer: ").Append(LayerMask.LayerToName(h.gameObject.layer))
          .Append(" (").Append(h.gameObject.layer).AppendLine(")");
        Vector3 p = h.transform.position;
        sb.AppendFormat("  Position: ({0:0.00}, {1:0.00}, {2:0.00})\n", p.x, p.y, p.z);

        var netObj = h.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            sb.Append("  NetworkObjectId: ").Append(netObj.NetworkObjectId)
              .Append(netObj.IsSpawned ? " <color=#64FF64>(spawned)</color>" : " <color=grey>(not spawned)</color>")
              .AppendLine();
        }
        else
        {
            sb.AppendLine("  NetworkObject: <color=grey>none</color>");
        }
    }

    private static void AppendHarvestableState(StringBuilder sb, Harvestable h)
    {
        sb.AppendLine("<b><color=#FFFFFF>Harvestable</color></b>");
        sb.Append("  Category: ").AppendLine(h.Category.ToString());
        sb.Append("  Can harvest: ").AppendLine(BoolColor(h.CanHarvest()));
        sb.Append("  Depleted: ").AppendLine(BoolColor(h.IsDepleted, invertColors: true));
        sb.Append("  Remaining yield: ").AppendLine(YieldString(h.RemainingYield));
        sb.AppendFormat("  Harvest duration: {0:0.00}s\n", h.HarvestDuration);
        sb.Append("  Harvest tool: ").AppendLine(h.RequiredHarvestTool != null
            ? h.RequiredHarvestTool.ItemName
            : "<color=grey>(none / bare hands)</color>");

        sb.Append("  Outputs:");
        AppendEntryList(sb, h.HarvestOutputs);
    }

    private static void AppendDestruction(StringBuilder sb, Harvestable h)
    {
        sb.AppendLine("<b><color=#FFFFFF>Destruction</color></b>");
        sb.Append("  Required tool: ").AppendLine(h.RequiredDestructionTool != null
            ? h.RequiredDestructionTool.ItemName
            : "<color=grey>(any)</color>");
        sb.AppendFormat("  Duration: {0:0.00}s\n", h.DestructionDuration);
        sb.Append("  Outputs:");
        AppendEntryList(sb, h.DestructionOutputs);
    }

    private static void AppendEntryList(StringBuilder sb, System.Collections.Generic.IReadOnlyList<HarvestOutputEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            sb.AppendLine(" <color=grey>(none)</color>");
            return;
        }
        sb.AppendLine();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            string name = e.Item != null ? e.Item.ItemName : "<color=grey>null</color>";
            sb.Append("    • ").Append(e.Count).Append("× ").AppendLine(name);
        }
    }

    private static void AppendCropSection(StringBuilder sb, Harvestable crop)
    {
        sb.AppendLine("<b><color=#FFFFFF>Crop</color></b>");

        var netSync = crop.GetComponent<HarvestableNetSync>();
        string cropId = netSync != null ? netSync.CropIdNet.Value.ToString() : "<color=grey>?</color>";
        int currentStage = netSync != null ? netSync.CurrentStage.Value : -1;
        bool cropDepleted = netSync != null && netSync.IsDepleted.Value;

        CropSO so = !string.IsNullOrEmpty(cropId) ? CropRegistry.Get(cropId) : null;

        sb.Append("  Crop id: ").AppendLine(string.IsNullOrEmpty(cropId) ? "<color=grey>(none)</color>" : cropId);
        if (so != null)
        {
            sb.Append("  Display name: ").AppendLine(so.DisplayName ?? "<color=grey>—</color>");
            int days = so.DaysToMature;
            int clamped = Mathf.Clamp(currentStage, 0, days);
            bool mature = clamped >= days;
            sb.Append("  Stage: ").Append(clamped).Append(" / ").Append(days)
              .Append(mature ? " <color=#64FF64>(mature)</color>" : " <color=#FFD080>(growing)</color>")
              .AppendLine();
            sb.Append("  Net depleted: ").AppendLine(BoolColor(cropDepleted, invertColors: true));
            sb.Append("  Perennial: ").AppendLine(BoolColor(so.IsPerennial));
            if (so.IsPerennial)
            {
                sb.Append("  Regrow days: ").Append(so.RegrowDays).AppendLine();
            }
            sb.AppendFormat("  Min moisture: {0:0.00}\n", so.MinMoistureForGrowth);
            sb.AppendFormat("  Plant duration: {0:0.00}s\n", so.PlantDuration);
            sb.Append("  Produce:");
            if (so.HarvestOutputs == null || so.HarvestOutputs.Count == 0)
            {
                sb.AppendLine(" <color=grey>(none)</color>");
            }
            else
            {
                sb.AppendLine();
                for (int i = 0; i < so.HarvestOutputs.Count; i++)
                {
                    var e = so.HarvestOutputs[i];
                    string n = e.Item is ItemSO it && it != null ? it.ItemName : "<color=grey>null</color>";
                    sb.Append("    • ").Append(e.Count).Append("× ").AppendLine(n);
                }
            }
        }
        else
        {
            sb.AppendLine("  <color=grey>CropSO not resolved (registry / netvar not yet replicated)</color>");
            if (currentStage >= 0)
                sb.Append("  Net stage: ").AppendLine(currentStage.ToString());
            sb.Append("  Net depleted: ").AppendLine(BoolColor(cropDepleted, invertColors: true));
        }

        sb.AppendLine();
        AppendCropCellSection(sb, crop);
    }

    private static void AppendCropCellSection(StringBuilder sb, Harvestable crop)
    {
        sb.AppendLine("<b><color=#FFFFFF>Terrain Cell</color></b>");
        sb.Append("  Cell: (").Append(crop.CellX).Append(", ").Append(crop.CellZ).AppendLine(")");

        TerrainCellGrid grid = crop.Grid;
        if (grid == null || grid.Width <= 0)
        {
            sb.AppendLine("  <color=grey>Grid unavailable on this peer (server-only field — clients see this empty until grid replicates).</color>");
            return;
        }

        if (crop.CellX < 0 || crop.CellX >= grid.Width || crop.CellZ < 0 || crop.CellZ >= grid.Depth)
        {
            sb.AppendLine("  <color=#FF6464>Cell coords out of range.</color>");
            return;
        }

        ref TerrainCell cell = ref grid.GetCellRef(crop.CellX, crop.CellZ);
        sb.Append("  Base type: ").AppendLine(string.IsNullOrEmpty(cell.BaseTypeId) ? "<color=grey>—</color>" : cell.BaseTypeId);
        sb.Append("  Current type: ").AppendLine(string.IsNullOrEmpty(cell.CurrentTypeId) ? "<color=grey>—</color>" : cell.CurrentTypeId);
        sb.Append("  Plowed: ").AppendLine(BoolColor(cell.IsPlowed));
        sb.Append("  Planted crop: ").AppendLine(string.IsNullOrEmpty(cell.PlantedCropId) ? "<color=grey>(none)</color>" : cell.PlantedCropId);
        sb.AppendFormat("  Moisture: {0:0.00}\n", cell.Moisture);
        sb.AppendFormat("  Temperature: {0:0.00}\n", cell.Temperature);
        sb.AppendFormat("  Snow depth: {0:0.00}\n", cell.SnowDepth);
        sb.AppendFormat("  Fertility: {0:0.00}\n", cell.Fertility);
        sb.AppendFormat("  Growth timer: {0:0.00} day(s)\n", cell.GrowthTimer);
        if (cell.TimeSinceLastWatered < 0f)
            sb.AppendLine("  Time since watered: <color=grey>(perennial inactive / not refilling)</color>");
        else
            sb.AppendFormat("  Time since watered: {0:0.00} day(s)\n", cell.TimeSinceLastWatered);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static string BoolColor(bool value, bool invertColors = false)
    {
        // invertColors=true means "true is bad, false is good" (e.g. depleted)
        bool good = invertColors ? !value : value;
        string color = good ? "#64FF64" : "#FF6464";
        return $"<color={color}>{(value ? "Yes" : "No")}</color>";
    }

    private static string YieldString(int remaining)
    {
        if (remaining == int.MaxValue) return "<color=#64FF64>∞ (non-depletable)</color>";
        if (remaining <= 0) return "<color=#FF6464>0</color>";
        return remaining.ToString();
    }
}
