using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// IBuildingInspectorView for any <see cref="Building"/> target. A single view that handles every
/// concrete subclass (Residential, Commercial subclasses) via runtime polymorphism, mirroring the
/// way <see cref="StorageFurnitureInspectorView"/> handles every <c>StorageFurniture</c> subtype.
/// Refreshes every frame; cheap because <see cref="DevInspectModule"/> keeps non-active views
/// disabled.
///
/// Renders sections (top to bottom):
///   1. Identity — concrete type, name, IDs, public flag, placed-by character.
///   2. State — construction state + materials (required / contributed / pending).
///   3. Owners — every entry from <see cref="Room.OwnerIds"/> resolved to a Character name.
///   4. Commercial — only when the target is a <see cref="CommercialBuilding"/>: jobs, on-shift
///      roster, owning Community, inventory count, operational flag.
///   5. Rooms — list of rooms with per-room furniture count.
///   6. Furniture — flat list across every room, prefixed with the room name.
///   7. Interior — HasInterior + interior MapId (if any).
/// </summary>
public class BuildingInspectorView : MonoBehaviour, IBuildingInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content;

    private Building _target;

    public bool CanInspect(Building target) => target != null;

    public void SetTarget(Building target)
    {
        _target = target;
        UpdateHeader();
    }

    public void Clear()
    {
        _target = null;
        UpdateHeader();
        if (_content != null) _content.text = "<color=grey>No building selected.</color>";
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
        string label = !string.IsNullOrEmpty(_target.BuildingName)
            ? _target.BuildingName
            : _target.gameObject.name;
        _headerLabel.text = $"Inspecting: {label}";
    }

    // ─── Rendering ────────────────────────────────────────────────────────

    private static string RenderContent(Building b)
    {
        var sb = new StringBuilder(2048);

        AppendIdentity(sb, b);
        sb.AppendLine();

        AppendState(sb, b);
        sb.AppendLine();

        AppendOwners(sb, b);
        sb.AppendLine();

        if (b is CommercialBuilding cb)
        {
            AppendCommercial(sb, cb);
            sb.AppendLine();
        }

        AppendRooms(sb, b);
        sb.AppendLine();

        AppendFurniture(sb, b);
        sb.AppendLine();

        AppendInterior(sb, b);

        return sb.ToString();
    }

    private static void AppendIdentity(StringBuilder sb, Building b)
    {
        sb.AppendLine("<b><color=#FFFFFF>Identity</color></b>");
        sb.Append("  <b>Type:</b> ").AppendLine(b.GetType().Name);
        sb.Append("  <b>Building Type:</b> ").AppendLine(b.BuildingType.ToString());
        sb.Append("  <b>Name:</b> ").AppendLine(string.IsNullOrEmpty(b.BuildingName) ? "<color=grey>—</color>" : b.BuildingName);
        sb.Append("  <b>GameObject:</b> ").AppendLine(b.gameObject.name);
        sb.Append("  <b>BuildingId:</b> ").AppendLine(string.IsNullOrEmpty(b.BuildingId) ? "<color=grey>—</color>" : b.BuildingId);
        sb.Append("  <b>PrefabId:</b> ").AppendLine(string.IsNullOrEmpty(b.PrefabId) ? "<color=grey>—</color>" : b.PrefabId);
        sb.Append("  <b>Public:</b> ").AppendLine(b.IsPublicLocation ? "Yes" : "No");

        string placedById = b.PlacedByCharacterId.Value.ToString();
        if (!string.IsNullOrEmpty(placedById))
        {
            var placedBy = Character.FindByUUID(placedById);
            string label = placedBy != null
                ? $"{placedBy.CharacterName} <color=#888888>({placedById})</color>"
                : $"<color=#888888>{placedById}</color> <color=grey>(not spawned)</color>";
            sb.Append("  <b>Placed by:</b> ").AppendLine(label);
        }
        else
        {
            sb.AppendLine("  <b>Placed by:</b> <color=grey>(scene-authored)</color>");
        }
    }

    private static void AppendState(StringBuilder sb, Building b)
    {
        sb.AppendLine("<b><color=#FFFFFF>State</color></b>");

        string stateColor = b.IsUnderConstruction ? "#FFB060" : "#64FF64";
        sb.Append("  <b>State:</b> <color=").Append(stateColor).Append(">").Append(b.CurrentState).AppendLine("</color>");

        var requirements = b.ConstructionRequirements;
        if (requirements == null || requirements.Count == 0)
        {
            sb.AppendLine("  <color=grey>(no construction requirements)</color>");
            return;
        }

        var contributed = b.ContributedMaterials;
        var pending = b.GetPendingMaterials();

        sb.AppendLine("  <b>Materials:</b>");
        for (int i = 0; i < requirements.Count; i++)
        {
            var req = requirements[i];
            ItemSO item = req.Item;
            int required = req.Amount;
            int got = (contributed != null && item != null && contributed.TryGetValue(item, out int c)) ? c : 0;
            int left = (pending != null && item != null && pending.TryGetValue(item, out int p)) ? p : 0;

            string itemName = item != null && !string.IsNullOrEmpty(item.ItemName) ? item.ItemName : (item != null ? item.name : "<null>");
            string color = left == 0 ? "#64FF64" : "#FFB060";
            sb.Append("    • <color=").Append(color).Append(">").Append(itemName).Append("</color> — ");
            sb.Append(got).Append(" / ").Append(required);
            if (left > 0) sb.Append(" <color=#FF6464>(").Append(left).Append(" left)</color>");
            sb.AppendLine();
        }
    }

    private static void AppendOwners(StringBuilder sb, Building b)
    {
        sb.AppendLine("<b><color=#FFFFFF>Owners</color></b>");

        // Building inherits Room.OwnerIds → IEnumerable<string> (NetworkList-backed).
        // Materialise once to count + index.
        var ownerIds = new List<string>();
        foreach (var id in b.OwnerIds)
        {
            if (!string.IsNullOrEmpty(id)) ownerIds.Add(id);
        }

        if (ownerIds.Count == 0)
        {
            sb.AppendLine("  <color=grey>(no owners)</color>");
            return;
        }

        for (int i = 0; i < ownerIds.Count; i++)
        {
            string id = ownerIds[i];
            var owner = Character.FindByUUID(id);
            if (owner != null)
            {
                sb.Append("  • ").Append(owner.CharacterName);
                sb.Append(" <color=#888888>(").Append(id).AppendLine(")</color>");
            }
            else
            {
                sb.Append("  • <color=#888888>").Append(id).AppendLine("</color> <color=grey>(not spawned)</color>");
            }
        }
    }

    private static void AppendCommercial(StringBuilder sb, CommercialBuilding cb)
    {
        sb.AppendLine("<b><color=#FFFFFF>Commercial</color></b>");

        sb.Append("  <b>Operational:</b> ").AppendLine(cb.IsOperational
            ? "<color=#64FF64>Yes</color>"
            : "<color=#FFB060>No</color>");

        var community = cb.OwnerCommunity;
        if (community != null)
        {
            sb.Append("  <b>Community:</b> ").AppendLine(string.IsNullOrEmpty(community.communityName) ? "<unnamed>" : community.communityName);
        }

        sb.Append("  <b>Inventory items:</b> ").Append(cb.InventoryTotalCount).AppendLine();

        // Jobs.
        var jobs = cb.Jobs;
        sb.Append("  <b>Jobs (").Append(jobs?.Count ?? 0).AppendLine("):</b>");
        if (jobs == null || jobs.Count == 0)
        {
            sb.AppendLine("    <color=grey>(no jobs)</color>");
        }
        else
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                if (job == null)
                {
                    sb.Append("    [").Append(i).AppendLine("] <color=grey>(null)</color>");
                    continue;
                }
                string title = !string.IsNullOrEmpty(job.JobTitle) ? job.JobTitle : job.GetType().Name;
                sb.Append("    [").Append(i).Append("] ").Append(title).Append(" — ");
                if (job.IsAssigned && job.Worker != null)
                {
                    sb.Append("<color=#64FF64>").Append(job.Worker.CharacterName).AppendLine("</color>");
                }
                else
                {
                    sb.AppendLine("<color=grey>unassigned</color>");
                }
            }
        }

        // Active workers on shift.
        var onShift = cb.ActiveWorkersOnShift;
        sb.Append("  <b>On shift (").Append(onShift?.Count ?? 0).AppendLine("):</b>");
        if (onShift == null || onShift.Count == 0)
        {
            sb.AppendLine("    <color=grey>(none punched in)</color>");
        }
        else
        {
            for (int i = 0; i < onShift.Count; i++)
            {
                var w = onShift[i];
                if (w == null) continue;
                sb.Append("    • ").AppendLine(w.CharacterName);
            }
        }

        // Time clock (cached on the building).
        if (cb.TimeClock != null)
        {
            sb.AppendLine("  <b>Time clock:</b> <color=#64FF64>present</color>");
        }
        else
        {
            sb.AppendLine("  <b>Time clock:</b> <color=grey>none</color>");
        }
    }

    private static void AppendRooms(StringBuilder sb, Building b)
    {
        sb.AppendLine("<b><color=#FFFFFF>Rooms</color></b>");
        int total = 0;
        foreach (var room in b.Rooms)
        {
            if (room == null) continue;
            total++;
            string roomName = !string.IsNullOrEmpty(room.RoomName) ? room.RoomName : room.gameObject.name;
            int furnitureCount = 0;
            if (room.FurnitureManager != null && room.FurnitureManager.Furnitures != null)
            {
                furnitureCount = room.FurnitureManager.Furnitures.Count;
            }
            sb.Append("  • ").Append(roomName).Append(" — ").Append(furnitureCount).AppendLine(" furniture");
        }
        if (total == 0)
        {
            sb.AppendLine("  <color=grey>(no rooms)</color>");
        }
    }

    private static void AppendFurniture(StringBuilder sb, Building b)
    {
        sb.AppendLine("<b><color=#FFFFFF>Furniture</color></b>");
        int total = 0;
        foreach (var room in b.Rooms)
        {
            if (room == null || room.FurnitureManager == null) continue;
            var list = room.FurnitureManager.Furnitures;
            if (list == null) continue;

            string roomName = !string.IsNullOrEmpty(room.RoomName) ? room.RoomName : room.gameObject.name;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (f == null) continue;
                total++;
                string fname = !string.IsNullOrEmpty(f.FurnitureName) ? f.FurnitureName : f.gameObject.name;
                sb.Append("  • <color=#888888>[").Append(roomName).Append("]</color> ");
                sb.Append(fname);
                sb.Append(" <color=#666666>(").Append(f.GetType().Name).AppendLine(")</color>");
            }
        }
        if (total == 0)
        {
            sb.AppendLine("  <color=grey>(no furniture)</color>");
        }
    }

    private static void AppendInterior(StringBuilder sb, Building b)
    {
        sb.AppendLine("<b><color=#FFFFFF>Interior</color></b>");

        // Authored support — does WorldSettingsData have an InteriorPrefab for this PrefabId?
        bool supported = b.SupportsInterior;
        if (supported)
        {
            sb.AppendLine("  <b>Supported:</b> <color=#64FF64>Yes</color> <color=#888888>(InteriorPrefab registered)</color>");
        }
        else if (string.IsNullOrEmpty(b.PrefabId))
        {
            sb.AppendLine("  <b>Supported:</b> <color=grey>N/A (no PrefabId — scene-static building)</color>");
        }
        else
        {
            sb.AppendLine("  <b>Supported:</b> <color=#FFB060>No (no InteriorPrefab in WorldSettingsData for this PrefabId)</color>");
        }

        // Runtime spawn — is a MapController for the interior currently registered?
        if (b.HasInterior)
        {
            string mapId = b.GetInteriorMapId();
            sb.Append("  <b>Spawned:</b> <color=#64FF64>Yes</color> ");
            sb.Append("<color=#888888>(MapId: ").Append(string.IsNullOrEmpty(mapId) ? "—" : mapId).AppendLine(")</color>");
        }
        else if (supported)
        {
            sb.AppendLine("  <b>Spawned:</b> <color=grey>No (lazy-spawn on first door entry)</color>");
        }
        else
        {
            sb.AppendLine("  <b>Spawned:</b> <color=grey>—</color>");
        }
    }
}
