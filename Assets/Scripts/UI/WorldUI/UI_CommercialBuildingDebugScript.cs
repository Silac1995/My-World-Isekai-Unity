using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// UI Debug Script for CommercialBuilding (Owner, Jobs, Workers, TaskManager state)
/// </summary>
public class UI_CommercialBuildingDebugScript : MonoBehaviour
{
    [SerializeField] private CommercialBuilding _building;
    [SerializeField] private TextMeshProUGUI _ownerText;
    [SerializeField] private TextMeshProUGUI _jobsText;
    [SerializeField] private TextMeshProUGUI _taskManagerStateText;
    [SerializeField] private TextMeshProUGUI _logisticsText;
    [SerializeField] private TextMeshProUGUI _inventoryText;

    private void Update()
    {
        if (_building == null) return;

        UpdateOwner();
        UpdateJobsAndWorkers();
        UpdateTaskManager();
        UpdateLogisticsManager();
        UpdateInventory();
    }

    private void UpdateOwner()
    {
        if (_ownerText == null) return;

        if (_building.Owner != null)
        {
            _ownerText.text = "Owner: " + _building.Owner.CharacterName;
            _ownerText.color = Color.yellow;
        }
        else if (_building.OwnerCommunity != null)
        {
            _ownerText.text = "Owner Community: " + _building.OwnerCommunity.communityName;
            _ownerText.color = Color.cyan;
        }
        else
        {
            _ownerText.text = "Owner: None";
            _ownerText.color = Color.gray;
        }
    }

    private void UpdateJobsAndWorkers()
    {
        if (_jobsText == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>Jobs & Workers:</b>");

        if (_building.Jobs == null || _building.Jobs.Count == 0)
        {
            sb.AppendLine("<color=#888888>No jobs available.</color>");
        }
        else
        {
            foreach (var job in _building.Jobs)
            {
                if (job.IsAssigned && job.Worker != null)
                {
                    bool onShift = false;
                    if (_building.ActiveWorkersOnShift != null)
                    {
                        onShift = _building.ActiveWorkersOnShift.Contains(job.Worker);
                    }
                    
                    string shiftStatus = onShift ? "<color=#00FF00>[On Shift]</color>" : "<color=#888888>[Off Shift]</color>";
                    sb.AppendLine($"- {job.JobTitle}: <color=#FFFF00>{job.Worker.CharacterName}</color> {shiftStatus}");
                    
                    if (!string.IsNullOrEmpty(job.CurrentActionName))
                    {
                        sb.AppendLine($"  └ <color=#00FFFF>Action:</color> {job.CurrentActionName}");
                    }
                }
                else
                {
                    sb.AppendLine($"- {job.JobTitle}: <color=#888888><i>Unassigned</i></color>");
                }
            }
        }

        _jobsText.text = sb.ToString();
    }

    private void UpdateTaskManager()
    {
        if (_taskManagerStateText == null) return;
        
        var taskManager = _building.TaskManager;
        if (taskManager == null)
        {
            _taskManagerStateText.text = "<color=#FF0000>No TaskManager attached.</color>";
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>Task Manager State:</b>");

        var availableTasks = taskManager.AvailableTasks;
        if (availableTasks != null)
        {
            sb.AppendLine($"<color=#00FFFF>Available ({availableTasks.Count}):</color>");
            foreach (var task in availableTasks)
            {
                string taskType = task.GetType().Name;
                string targetName = task.Target != null ? task.Target.name : "Null Target";
                sb.AppendLine($"  - {taskType} -> {targetName}");
            }
        }

        var inProgressTasks = taskManager.InProgressTasks;
        if (inProgressTasks != null)
        {
            sb.AppendLine($"<color=#FFA500>In Progress ({inProgressTasks.Count}):</color>");
            foreach (var task in inProgressTasks)
            {
                string taskType = task.GetType().Name;
                string targetName = task.Target != null ? task.Target.name : "Null Target";
                string workerName = task.ClaimedBy != null ? task.ClaimedBy.CharacterName : "Unknown Worker";
                sb.AppendLine($"  - {taskType} -> {targetName} <color=#00FF00>[{workerName}]</color>");
            }
        }

        _taskManagerStateText.text = sb.ToString();
    }

    private void UpdateLogisticsManager()
    {
        if (_logisticsText == null) return;

        var logistics = _building.GetComponent<BuildingLogisticsManager>();
        if (logistics == null)
        {
            _logisticsText.text = "<color=#888888>No LogisticsManager attached.</color>";
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>Logistics Manager:</b>");

        if (logistics.HasPendingOrders)
        {
            sb.AppendLine("<color=#FFA500>[!] Has Pending Orders in Queue</color>");
        }

        var activeOrders = logistics.ActiveOrders;
        if (activeOrders != null && activeOrders.Count > 0)
        {
            sb.AppendLine($"<color=#00FFFF>Active Buy Orders Received ({activeOrders.Count}):</color>");
            foreach (var o in activeOrders)
            {
                string itemName = o.ItemToTransport != null ? o.ItemToTransport.ItemName : "???";
                string clientName = o.Destination != null ? o.Destination.BuildingName : "???";
                string sourceName = o.Source != null ? o.Source.BuildingName : "???";
                string clientBoss = o.ClientBoss != null ? o.ClientBoss.CharacterName : "None";
                string interBoss = o.IntermediaryBoss != null ? o.IntermediaryBoss.CharacterName : "None";

                sb.AppendLine($"  - {itemName} <color=#888888>[{o.DeliveredQuantity} Deliv | {o.DispatchedQuantity} Disp | {o.Quantity} Total]</color>");
                sb.AppendLine($"    └ <color=#888888>Src: {sourceName} | Dst: {clientName}</color>");
                sb.AppendLine($"    └ <color=#888888>Days: {o.RemainingDays} | Boss: {clientBoss} | Inter: {interBoss}</color>");
            }
        }

        var placedBuy = logistics.PlacedBuyOrders;
        if (placedBuy != null && placedBuy.Count > 0)
        {
            sb.AppendLine($"<color=#FFFF00>Placed Buy Orders ({placedBuy.Count}):</color>");
            foreach (var o in placedBuy)
            {
                string itemName = o.ItemToTransport != null ? o.ItemToTransport.ItemName : "???";
                string supplierName = o.Source != null ? o.Source.BuildingName : "???";
                string clientName = o.Destination != null ? o.Destination.BuildingName : "???";
                string clientBoss = o.ClientBoss != null ? o.ClientBoss.CharacterName : "None";
                string interBoss = o.IntermediaryBoss != null ? o.IntermediaryBoss.CharacterName : "None";

                sb.AppendLine($"  - {itemName} <color=#888888>[{o.DeliveredQuantity} Deliv | {o.DispatchedQuantity} Disp | {o.Quantity} Total]</color>");
                sb.AppendLine($"    └ <color=#888888>Src: {supplierName} | Dst: {clientName}</color>");
                sb.AppendLine($"    └ <color=#888888>Days: {o.RemainingDays} | Boss: {clientBoss} | Inter: {interBoss}</color>");
            }
        }

        var placedTransport = logistics.PlacedTransportOrders;
        if (placedTransport != null && placedTransport.Count > 0)
        {
            sb.AppendLine($"<color=#00FF00>Placed Transport ({placedTransport.Count}):</color>");
            foreach (var o in placedTransport)
            {
                string itemName = o.ItemToTransport != null ? o.ItemToTransport.ItemName : "???";
                string destName = o.Destination != null ? o.Destination.BuildingName : "???";
                string sourceName = o.Source != null ? o.Source.BuildingName : "???";
                
                sb.AppendLine($"  - {itemName} <color=#888888>[{o.DeliveredQuantity} Deliv | {o.InTransitQuantity} Transit | {o.Quantity} Total]</color>");
                sb.AppendLine($"    └ <color=#888888>Src: {sourceName} | Dst: {destName}</color>");
            }
        }

        var activeTransport = logistics.ActiveTransportOrders;
        if (activeTransport != null && activeTransport.Count > 0)
        {
            sb.AppendLine($"<color=#00FFFF>Active Transport Received ({activeTransport.Count}):</color>");
            foreach (var o in activeTransport)
            {
                string itemName = o.ItemToTransport != null ? o.ItemToTransport.ItemName : "???";
                string destName = o.Destination != null ? o.Destination.BuildingName : "???";
                string sourceName = o.Source != null ? o.Source.BuildingName : "???";
                
                sb.AppendLine($"  - {itemName} <color=#888888>[{o.DeliveredQuantity} Deliv | {o.InTransitQuantity} Transit | {o.Quantity} Total]</color>");
                sb.AppendLine($"    └ <color=#888888>Src: {sourceName} | Dst: {destName}</color>");
            }
        }

        var activeCrafting = logistics.ActiveCraftingOrders;
        if (activeCrafting != null && activeCrafting.Count > 0)
        {
            sb.AppendLine($"<color=#FFA500>Active Crafting ({activeCrafting.Count}):</color>");
            foreach (var o in activeCrafting)
            {
                string itemName = o.ItemToCraft != null ? o.ItemToCraft.ItemName : "???";
                sb.AppendLine($"  - {o.CraftedQuantity}/{o.Quantity}x {itemName} [Completed: {o.IsCompleted}]");
            }
        }

        if (activeOrders.Count == 0 && placedBuy.Count == 0 && placedTransport.Count == 0 && activeTransport.Count == 0 && activeCrafting.Count == 0 && !logistics.HasPendingOrders)
        {
            sb.AppendLine("<color=#888888>No active logistics operations.</color>");
        }

        _logisticsText.text = sb.ToString();
    }

    private void UpdateInventory()
    {
        if (_inventoryText == null) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b>Storage Inventory:</b>");

        var inventory = _building.Inventory;
        if (inventory == null || inventory.Count == 0)
        {
            sb.AppendLine("<color=#888888>Empty.</color>");
        }
        else
        {
            var grouped = inventory
                .Where(i => i != null && i.ItemSO != null)
                .GroupBy(i => i.ItemSO.ItemName);

            sb.AppendLine($"<color=#00FF00>Total Items: {inventory.Count}</color>");
            foreach (var group in grouped)
            {
                sb.AppendLine($"  - {group.Key}: {group.Count()}");
            }
        }

        _inventoryText.text = sb.ToString();
    }
}
