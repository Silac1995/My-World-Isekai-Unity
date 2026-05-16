using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Read-only Overview sub-tab. Renders the full 11-section Building dump into a
/// single TMP_Text — Identity / State / Owners / Commercial / Inventory /
/// Wanted Resources / Tracked Harvestables / Needed Resources / Logistics
/// Orders / Tasks / Rooms / Furniture / Interior.
///
/// Verbatim port of the prior render code from <see cref="BuildingInspectorView"/>;
/// behavior unchanged. The TMP_Text it writes into is the same field that used to
/// live on the parent view — the parent re-parents it under this sub-tab's host
/// at runtime and calls <see cref="SetContentLabel"/> to wire it.
/// </summary>
public sealed class BuildingOverviewSubTab : BuildingSubTab
{
    [SerializeField] private TMP_Text _content;

    /// <summary>Wired by <see cref="BuildingInspectorView"/> when it builds the sub-tab.</summary>
    public void SetContentLabel(TMP_Text content) { _content = content; }

    protected override void DoRefresh(Building b)
    {
        if (_content == null) return;
        _content.text = BuildOverviewText(b);
    }

    protected override void DoClear()
    {
        if (_content != null) _content.text = "<color=grey>No building selected.</color>";
    }

    // ─── Rendering ────────────────────────────────────────────────────────

    private static string BuildOverviewText(Building b)
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

            AppendInventory(sb, cb);
            sb.AppendLine();

            if (cb is HarvestingBuilding hb)
            {
                AppendWantedResources(sb, hb);
                sb.AppendLine();

                AppendTrackedHarvestables(sb, hb);
                sb.AppendLine();
            }

            AppendNeededResources(sb, cb);
            sb.AppendLine();

            AppendLogisticsOrders(sb, cb);
            sb.AppendLine();

            AppendTasks(sb, cb);
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
        sb.Append("  <b>Blueprint:</b> ").AppendLine(b.Blueprint != null
            ? $"{b.Blueprint.name} <color=#888888>({b.Blueprint.PrefabId})</color>"
            : "<color=#FFB060>&lt;missing&gt;</color>");
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

        float progress01 = Mathf.Clamp01(b.ConstructionProgress.Value);
        string progressColor = progress01 >= 1f ? "#64FF64" : "#FFB060";
        sb.Append("  <b>Progress:</b> <color=").Append(progressColor).Append(">")
          .Append((progress01 * 100f).ToString("F1")).AppendLine("%</color>");

        var contributed = b.ContributedMaterials;
        var pending = b.GetPendingMaterials();
        var delivered = b.DeliveredMaterials;

        sb.AppendLine("  <b>Materials:</b>");
        for (int i = 0; i < requirements.Count; i++)
        {
            var req = requirements[i];
            ItemSO item = req.Item;
            int required = req.Amount;
            int got = (contributed != null && item != null && contributed.TryGetValue(item, out int c)) ? c : 0;
            int left = (pending != null && item != null && pending.TryGetValue(item, out int p)) ? p : 0;

            int deliveredQty = 0;
            if (delivered != null)
            {
                for (int j = 0; j < delivered.Count; j++)
                {
                    var entry = delivered[j];
                    if (entry.RequirementIndex == i) { deliveredQty = entry.Delivered; break; }
                }
            }

            string itemName = item != null && !string.IsNullOrEmpty(item.ItemName) ? item.ItemName : (item != null ? item.name : "<null>");
            string color = left == 0 ? "#64FF64" : "#FFB060";
            sb.Append("    • <color=").Append(color).Append(">").Append(itemName).Append("</color> — ");
            sb.Append("contrib ").Append(got).Append(" / ").Append(required);
            sb.Append(" <color=#888888>(replicated: ").Append(deliveredQty).Append(")</color>");
            if (left > 0) sb.Append(" <color=#FF6464>(").Append(left).Append(" left)</color>");
            sb.AppendLine();
        }
    }

    private static void AppendOwners(StringBuilder sb, Building b)
    {
        sb.AppendLine("<b><color=#FFFFFF>Owners</color></b>");

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

                    string goal = job.CurrentGoalName;
                    string action = job.CurrentActionName;
                    bool hasGoal = !string.IsNullOrEmpty(goal);
                    bool hasAction = !string.IsNullOrEmpty(action);
                    if (hasGoal || hasAction)
                    {
                        sb.Append("        <color=#888888>goal:</color> ");
                        sb.Append(hasGoal ? goal : "<color=grey>—</color>");
                        sb.Append("  <color=#888888>action:</color> ");
                        sb.AppendLine(hasAction ? action : "<color=grey>—</color>");
                    }
                }
                else
                {
                    sb.AppendLine("<color=grey>unassigned</color>");
                }
            }
        }

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

        if (cb.TimeClock != null)
        {
            sb.AppendLine("  <b>Time clock:</b> <color=#64FF64>present</color>");
        }
        else
        {
            sb.AppendLine("  <b>Time clock:</b> <color=grey>none</color>");
        }
    }

    private static void AppendInventory(StringBuilder sb, CommercialBuilding cb)
    {
        sb.AppendLine("<b><color=#FFFFFF>Inventory</color></b>");

        var counts = cb.GetInventoryCountsByItemSO();
        if (counts == null || counts.Count == 0)
        {
            sb.AppendLine("  <color=grey>(empty)</color>");
            return;
        }

        int totalUnits = 0;
        foreach (var v in counts.Values) totalUnits += v;

        sb.Append("  <b>Total:</b> ").Append(totalUnits);
        sb.Append(" <color=#888888>across ").Append(counts.Count).AppendLine(" item type(s)</color>");

        var entries = new List<KeyValuePair<ItemSO, int>>(counts);
        entries.Sort((a, b) => b.Value.CompareTo(a.Value));

        for (int i = 0; i < entries.Count; i++)
        {
            var kvp = entries[i];
            ItemSO item = kvp.Key;
            if (item == null) continue;
            string itemName = !string.IsNullOrEmpty(item.ItemName) ? item.ItemName : item.name;
            sb.Append("  • ").Append(itemName);
            sb.Append(" <color=#888888>x").Append(kvp.Value).AppendLine("</color>");
        }
    }

    private static void AppendWantedResources(StringBuilder sb, HarvestingBuilding hb)
    {
        sb.AppendLine("<b><color=#FFFFFF>Wanted Resources</color></b>");

        var wanted = hb.WantedResources;
        if (wanted == null || wanted.Count == 0)
        {
            sb.AppendLine("  <color=grey>(no wanted resources authored)</color>");
            return;
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            var entry = wanted[i];
            if (entry == null || entry.targetItem == null)
            {
                sb.AppendLine("  • <color=grey>(unset entry)</color>");
                continue;
            }

            string itemName = !string.IsNullOrEmpty(entry.targetItem.ItemName)
                ? entry.targetItem.ItemName
                : entry.targetItem.name;
            int have = hb.GetItemCount(entry.targetItem);
            string capLabel = entry.maxQuantity < 0 ? "∞" : entry.maxQuantity.ToString();

            sb.Append("  • ").Append(itemName);
            sb.Append(" <color=#888888>cap: ").Append(capLabel).Append("</color>");
            sb.Append("  <color=#888888>current: ").Append(have).Append("</color>");
            if (entry.maxQuantity >= 0)
            {
                bool atOrOver = have >= entry.maxQuantity;
                string flagColor = atOrOver ? "#64FF64" : "#FFB060";
                string flagText = atOrOver ? "[at/over cap]" : "[under cap]";
                sb.Append(" <color=").Append(flagColor).Append(">").Append(flagText).Append("</color>");
            }
            else
            {
                sb.Append(" <color=#888888>[unlimited]</color>");
            }
            sb.AppendLine();
        }
    }

    private static void AppendTrackedHarvestables(StringBuilder sb, HarvestingBuilding hb)
    {
        sb.AppendLine("<b><color=#FFFFFF>Tracked Harvestables</color></b>");

        Zone scanZone = hb.HarvestableZone ?? hb.HarvestingAreaZone;
        if (scanZone == null)
        {
            sb.AppendLine("  <color=grey>(no harvestable zone assigned)</color>");
        }
        else
        {
            string zoneName = !string.IsNullOrEmpty(scanZone.zoneName) ? scanZone.zoneName : scanZone.gameObject.name;
            sb.Append("  <b>Zone:</b> ").AppendLine(zoneName);
        }

        var nodes = hb.TrackedHarvestables;
        if (nodes == null || nodes.Count == 0)
        {
            sb.AppendLine("  <color=grey>(no nodes tracked — try ScanHarvestingArea on a new day, or assign a zone)</color>");
            return;
        }

        int alive = 0;
        int depleted = 0;
        var perItemAliveYield = new Dictionary<ItemSO, int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null) continue;
            if (n.IsDepleted) depleted++; else alive++;
            if (!n.IsDepleted)
            {
                var outs = n.HarvestOutputs;
                if (outs != null)
                {
                    for (int o = 0; o < outs.Count; o++)
                    {
                        var entry = outs[o];
                        if (entry.Item == null) continue;
                        if (!perItemAliveYield.ContainsKey(entry.Item)) perItemAliveYield[entry.Item] = 0;
                        int remaining = n.RemainingYield;
                        if (remaining == int.MaxValue)
                        {
                            perItemAliveYield[entry.Item] = -1;
                        }
                        else if (perItemAliveYield[entry.Item] != -1)
                        {
                            perItemAliveYield[entry.Item] += entry.Count * remaining;
                        }
                    }
                }
            }
        }

        sb.Append("  <b>Total:</b> ").Append(nodes.Count);
        sb.Append(" <color=#888888>(alive: ").Append(alive).Append(", depleted: ").Append(depleted).AppendLine(")</color>");

        if (perItemAliveYield.Count > 0)
        {
            sb.AppendLine("  <b>Estimated alive yield:</b>");
            foreach (var kvp in perItemAliveYield)
            {
                string itemName = !string.IsNullOrEmpty(kvp.Key.ItemName) ? kvp.Key.ItemName : kvp.Key.name;
                string total = kvp.Value < 0 ? "∞" : kvp.Value.ToString();
                sb.Append("    • ").Append(itemName).Append(": ").AppendLine(total);
            }
        }

        sb.AppendLine("  <b>Nodes:</b>");
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n == null) { sb.AppendLine("    <color=grey>(null)</color>"); continue; }

            string nodeLabel = ResolveHarvestableLabel(n);
            string stateColor = n.IsDepleted ? "#FF6464" : "#64FF64";
            string stateText = n.IsDepleted ? "depleted" : "alive";
            string yieldText = n.RemainingYield == int.MaxValue ? "∞" : n.RemainingYield.ToString();

            sb.Append("    [").Append(i.ToString("00")).Append("] ").Append(nodeLabel);
            sb.Append("  <color=").Append(stateColor).Append(">").Append(stateText).Append("</color>");
            sb.Append("  <color=#888888>yield: ").Append(yieldText).Append("</color>");
            sb.Append("  <color=#888888>cat: ").Append(n.Category).Append("</color>");
            if (n.IsCellCoupled)
            {
                sb.Append("  <color=#888888>cell: (").Append(n.CellX).Append(",").Append(n.CellZ).Append(")</color>");
            }
            sb.AppendLine();
        }
    }

    private static string ResolveHarvestableLabel(Harvestable h)
    {
        if (h.SO != null && !string.IsNullOrEmpty(h.SO.name)) return h.SO.name;
        var outs = h.HarvestOutputs;
        if (outs != null)
        {
            for (int i = 0; i < outs.Count; i++)
            {
                var e = outs[i];
                if (e.Item != null && !string.IsNullOrEmpty(e.Item.ItemName)) return e.Item.ItemName;
            }
        }
        return h.gameObject.name;
    }

    private static void AppendNeededResources(StringBuilder sb, CommercialBuilding cb)
    {
        sb.AppendLine("<b><color=#FFFFFF>Needed Resources</color></b>");

        bool anyContent = false;

        if (cb is HarvestingBuilding hb)
        {
            var stillWanted = hb.GetWantedItems();
            sb.Append("  <b>Harvest backlog (").Append(stillWanted?.Count ?? 0).AppendLine("):</b>");
            if (stillWanted == null || stillWanted.Count == 0)
            {
                sb.AppendLine("    <color=grey>(all wanted resources at cap)</color>");
            }
            else
            {
                for (int i = 0; i < stillWanted.Count; i++)
                {
                    var item = stillWanted[i];
                    if (item == null) continue;
                    string itemName = !string.IsNullOrEmpty(item.ItemName) ? item.ItemName : item.name;
                    int have = hb.GetItemCount(item);
                    sb.Append("    • ").Append(itemName);
                    sb.Append(" <color=#888888>have: ").Append(have).Append("</color>");
                    sb.AppendLine();
                }
            }
            anyContent = true;
        }

        var lm = cb.LogisticsManager;
        var craftingOrders = lm != null ? lm.ActiveCraftingOrders : null;
        var ingredientNeeds = new Dictionary<ItemSO, int>();
        if (craftingOrders != null)
        {
            for (int i = 0; i < craftingOrders.Count; i++)
            {
                var o = craftingOrders[i];
                if (o == null || o.IsCompleted || o.ItemToCraft == null) continue;
                var recipe = o.ItemToCraft.CraftingRecipe;
                if (recipe == null) continue;

                int remainingQty = o.Quantity - o.CraftedQuantity;
                if (remainingQty <= 0) continue;

                for (int r = 0; r < recipe.Count; r++)
                {
                    var ing = recipe[r];
                    if (ing.Item == null) continue;
                    if (!ingredientNeeds.ContainsKey(ing.Item)) ingredientNeeds[ing.Item] = 0;
                    ingredientNeeds[ing.Item] += ing.Amount * remainingQty;
                }
            }
        }

        if (ingredientNeeds.Count > 0)
        {
            sb.Append("  <b>Crafting deficit (").Append(ingredientNeeds.Count).AppendLine(" ingredient(s)):</b>");
            foreach (var kvp in ingredientNeeds)
            {
                ItemSO item = kvp.Key;
                int needed = kvp.Value;
                int have = cb.GetItemCount(item);
                int deficit = needed - have;

                string itemName = !string.IsNullOrEmpty(item.ItemName) ? item.ItemName : item.name;
                string color = deficit <= 0 ? "#64FF64" : "#FFB060";
                sb.Append("    • <color=").Append(color).Append(">").Append(itemName).Append("</color>");
                sb.Append("  <color=#888888>need: ").Append(needed).Append("</color>");
                sb.Append("  <color=#888888>have: ").Append(have).Append("</color>");
                if (deficit > 0)
                {
                    sb.Append(" <color=#FFB060>deficit: ").Append(deficit).Append("</color>");
                }
                else
                {
                    sb.Append(" <color=#64FF64>covered</color>");
                }
                sb.AppendLine();
            }
            anyContent = true;
        }

        if (!anyContent)
        {
            sb.AppendLine("  <color=grey>(no active demand — no harvest backlog and no crafting orders)</color>");
        }
    }

    private static void AppendLogisticsOrders(StringBuilder sb, CommercialBuilding cb)
    {
        sb.AppendLine("<b><color=#FFFFFF>Logistics Orders</color></b>");

        var lm = cb.LogisticsManager;
        if (lm == null)
        {
            sb.AppendLine("  <color=grey>(no logistics manager)</color>");
            return;
        }

        AppendBuyOrderList(sb, "Active Buy Orders (we are vendor)", lm.ActiveOrders, cb);
        AppendBuyOrderList(sb, "Placed Buy Orders (we are customer)", lm.PlacedBuyOrders, cb);
        AppendCraftingOrderList(sb, "Active Crafting Orders", lm.ActiveCraftingOrders, cb);
        AppendTransportOrderList(sb, "Active Transport Orders", lm.ActiveTransportOrders, cb);
        AppendTransportOrderList(sb, "Placed Transport Orders", lm.PlacedTransportOrders, cb);
    }

    private static void AppendBuyOrderList(StringBuilder sb, string label, IReadOnlyList<BuyOrder> orders, CommercialBuilding self)
    {
        sb.Append("  <b>").Append(label).Append(" (").Append(orders?.Count ?? 0).AppendLine("):</b>");
        if (orders == null || orders.Count == 0)
        {
            sb.AppendLine("    <color=grey>(none)</color>");
            return;
        }
        for (int i = 0; i < orders.Count; i++)
        {
            var o = orders[i];
            if (o == null) { sb.AppendLine("    <color=grey>(null)</color>"); continue; }
            string item = o.ItemToTransport != null && !string.IsNullOrEmpty(o.ItemToTransport.ItemName)
                ? o.ItemToTransport.ItemName
                : (o.ItemToTransport != null ? o.ItemToTransport.name : "<null>");
            string src = OtherBuildingLabel(o.Source, self);
            string dst = OtherBuildingLabel(o.Destination, self);
            string color = o.IsCompleted ? "#64FF64" : (o.IsReachabilityStalled ? "#FF6464" : "#FFB060");
            sb.Append("    • <color=").Append(color).Append(">").Append(item).Append(" x").Append(o.Quantity).Append("</color>");
            sb.Append("  <color=#888888>").Append(o.DeliveredQuantity).Append("/").Append(o.Quantity).Append(" delivered</color>");
            if (o.DispatchedQuantity > 0)
            {
                sb.Append(" <color=#888888>(").Append(o.DispatchedQuantity).Append(" en-route)</color>");
            }
            sb.AppendLine();
            sb.Append("        <color=#888888>").Append(src).Append(" → ").Append(dst).Append("</color>");
            sb.Append(" <color=#888888>days left:").Append(o.RemainingDays).Append("</color>");
            if (!o.IsPlaced) sb.Append(" <color=#FFB060>[unplaced]</color>");
            if (o.IsReachabilityStalled) sb.Append(" <color=#FF6464>[reach-stalled]</color>");
            sb.AppendLine();
        }
    }

    private static void AppendTransportOrderList(StringBuilder sb, string label, IReadOnlyList<TransportOrder> orders, CommercialBuilding self)
    {
        sb.Append("  <b>").Append(label).Append(" (").Append(orders?.Count ?? 0).AppendLine("):</b>");
        if (orders == null || orders.Count == 0)
        {
            sb.AppendLine("    <color=grey>(none)</color>");
            return;
        }
        for (int i = 0; i < orders.Count; i++)
        {
            var o = orders[i];
            if (o == null) { sb.AppendLine("    <color=grey>(null)</color>"); continue; }
            string item = o.ItemToTransport != null && !string.IsNullOrEmpty(o.ItemToTransport.ItemName)
                ? o.ItemToTransport.ItemName
                : (o.ItemToTransport != null ? o.ItemToTransport.name : "<null>");
            string src = OtherBuildingLabel(o.Source, self);
            string dst = OtherBuildingLabel(o.Destination, self);
            string color = o.IsCompleted ? "#64FF64" : "#FFB060";
            sb.Append("    • <color=").Append(color).Append(">").Append(item).Append(" x").Append(o.Quantity).Append("</color>");
            sb.Append("  <color=#888888>").Append(o.DeliveredQuantity).Append("/").Append(o.Quantity).Append(" delivered</color>");
            if (o.InTransitQuantity > 0)
            {
                sb.Append(" <color=#888888>(").Append(o.InTransitQuantity).Append(" in-transit)</color>");
            }
            var contributors = o.Contributors;
            if (contributors != null && contributors.Count > 0 && contributors[0] != null)
            {
                sb.Append(" <color=#888888>by ").Append(contributors[0].CharacterName).Append("</color>");
            }
            sb.AppendLine();
            sb.Append("        <color=#888888>").Append(src).Append(" → ").Append(dst).Append("</color>");
            if (!o.IsPlaced) sb.Append(" <color=#FFB060>[unplaced]</color>");
            sb.AppendLine();
        }
    }

    private static void AppendCraftingOrderList(StringBuilder sb, string label, IReadOnlyList<CraftingOrder> orders, CommercialBuilding self)
    {
        sb.Append("  <b>").Append(label).Append(" (").Append(orders?.Count ?? 0).AppendLine("):</b>");
        if (orders == null || orders.Count == 0)
        {
            sb.AppendLine("    <color=grey>(none)</color>");
            return;
        }
        for (int i = 0; i < orders.Count; i++)
        {
            var o = orders[i];
            if (o == null) { sb.AppendLine("    <color=grey>(null)</color>"); continue; }
            string item = o.ItemToCraft != null && !string.IsNullOrEmpty(o.ItemToCraft.ItemName)
                ? o.ItemToCraft.ItemName
                : (o.ItemToCraft != null ? o.ItemToCraft.name : "<null>");
            string customer = OtherBuildingLabel(o.CustomerBuilding, self);
            string color = o.IsCompleted ? "#64FF64" : (o.IsExpired ? "#FF6464" : "#FFB060");
            sb.Append("    • <color=").Append(color).Append(">").Append(item).Append(" x").Append(o.Quantity).Append("</color>");
            sb.Append("  <color=#888888>").Append(o.CraftedQuantity).Append("/").Append(o.Quantity).Append(" crafted</color>");
            sb.AppendLine();
            sb.Append("        <color=#888888>for ").Append(customer).Append("  days left:").Append(o.RemainingDays).Append("</color>");
            if (o.Contribution != null && o.Contribution.Count > 0)
            {
                sb.Append(" <color=#888888>contribs:");
                bool first = true;
                foreach (var kvp in o.Contribution)
                {
                    var c = Character.FindByUUID(kvp.Key);
                    string n = c != null ? c.CharacterName : kvp.Key;
                    if (!first) sb.Append(",");
                    sb.Append(" ").Append(n).Append("×").Append(kvp.Value);
                    first = false;
                }
                sb.Append("</color>");
            }
            if (!o.IsPlaced) sb.Append(" <color=#FFB060>[unplaced]</color>");
            sb.AppendLine();
        }
    }

    private static string OtherBuildingLabel(CommercialBuilding other, CommercialBuilding self)
    {
        if (other == null) return "<null>";
        if (other == self) return "<this>";
        return !string.IsNullOrEmpty(other.BuildingName) ? other.BuildingName : other.gameObject.name;
    }

    private static void AppendTasks(StringBuilder sb, CommercialBuilding cb)
    {
        sb.AppendLine("<b><color=#FFFFFF>Tasks</color></b>");

        var tm = cb.TaskManager;
        if (tm == null)
        {
            sb.AppendLine("  <color=grey>(no task manager)</color>");
            return;
        }

        AppendTaskList(sb, "Available", tm.AvailableTasks, isInProgress: false);
        AppendTaskList(sb, "In Progress", tm.InProgressTasks, isInProgress: true);
    }

    private static void AppendTaskList(StringBuilder sb, string label, IReadOnlyList<BuildingTask> tasks, bool isInProgress)
    {
        sb.Append("  <b>").Append(label).Append(" (").Append(tasks?.Count ?? 0).AppendLine("):</b>");
        if (tasks == null || tasks.Count == 0)
        {
            sb.AppendLine("    <color=grey>(none)</color>");
            return;
        }
        for (int i = 0; i < tasks.Count; i++)
        {
            var t = tasks[i];
            if (t == null) { sb.AppendLine("    <color=grey>(null)</color>"); continue; }
            string title = !string.IsNullOrEmpty(t.Title) ? t.Title : t.GetType().Name;
            string targetName = t.Target != null ? t.Target.gameObject.name : "<no target>";
            string color = isInProgress ? "#64FF64" : "#FFB060";
            sb.Append("    • <color=").Append(color).Append(">").Append(title).Append("</color>");
            sb.Append(" <color=#888888>→ ").Append(targetName).Append("</color>");
            if (t.ClaimedByWorkers != null && t.ClaimedByWorkers.Count > 0)
            {
                sb.Append(" <color=#888888>by ");
                for (int w = 0; w < t.ClaimedByWorkers.Count; w++)
                {
                    var ch = t.ClaimedByWorkers[w];
                    if (w > 0) sb.Append(", ");
                    sb.Append(ch != null ? ch.CharacterName : "<null>");
                }
                sb.Append("</color>");
            }
            sb.AppendLine();
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
                sb.Append(" <color=#666666>(").Append(f.GetType().Name).Append(")</color>");
                // For StorageFurniture, append the current Role so dev-mode inspect
                // reflects both player-UI dropdown changes and NPC shift-punch
                // auto-assignment writes (BuildingLogisticsManager.AssignStorageRolesForShift).
                // DoRefresh is called every frame by BuildingInspectorView.Update, so a
                // role flip on the next replication tick is picked up automatically.
                if (f is StorageFurniture sf)
                {
                    sb.Append(" <color=#FFB060>role=").Append(sf.Role).Append("</color>");
                }
                sb.AppendLine();
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
