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

    private void Update()
    {
        if (_building == null) return;

        UpdateOwner();
        UpdateJobsAndWorkers();
        UpdateTaskManager();
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
}
