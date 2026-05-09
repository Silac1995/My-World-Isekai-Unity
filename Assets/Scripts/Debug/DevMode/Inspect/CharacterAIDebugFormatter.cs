using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Pure formatting helpers for character AI debug state. Shared by the in-world
/// <c>UI_CharacterDebugScript</c> and the dev-mode AI inspector sub-tab so both show identical strings.
/// All methods tolerate a null character by returning a safe placeholder.
/// </summary>
public static class CharacterAIDebugFormatter
{
    public static string FormatAction(Character c)
    {
        if (c == null || c.CharacterActions == null) return "Action: N/A";
        var current = c.CharacterActions.CurrentAction;
        if (current != null) return $"<color=#FFFF00>Action: {current.GetType().Name}</color>";
        return "Action: Idle";
    }

    public static string FormatBehaviourStack(Character c)
    {
        if (c == null) return "IA: N/A";
        var controller = c.Controller as NPCController;
        if (controller == null) return "IA: <color=grey>PLAYER</color>";

        var stackNames = controller.GetBehaviourStackNames();
        if (stackNames == null || !stackNames.Any()) return "<color=grey>IA: Empty Stack</color>";

        string current = "<color=#00FFFF>Current: " + stackNames.First() + "</color>";
        string next = stackNames.Skip(1).Any()
            ? "\n<color=#F5B027>Queue: " + string.Join(" -> ", stackNames.Skip(1)) + "</color>"
            : "";
        return current + next;
    }

    public static string FormatInteraction(Character c)
    {
        if (c == null || c.CharacterInteraction == null) return "Interaction with: N/A";
        if (c.CharacterInteraction.IsInteracting && c.CharacterInteraction.CurrentTarget != null)
            return $"<color=#00FF00>Interaction with: {c.CharacterInteraction.CurrentTarget.CharacterName}</color>";
        return "<color=grey>Interaction with: None</color>";
    }

    public static string FormatAgent(Character c)
    {
        if (c == null) return "Agent: N/A";
        if (c.IsPlayer()) return "Agent: <color=grey>PLAYER (Manual)</color>";

        var controller = c.GetComponent<CharacterGameController>();
        if (controller != null && controller.Agent != null && controller.Agent.isOnNavMesh)
        {
            var agent = controller.Agent;
            string stopped = agent.isStopped ? "<color=red>STOPPED</color>" : "<color=green>RUNNING</color>";
            string path = agent.hasPath ? "Has Path" : "No Path";
            return $"Agent: {stopped} | {path}";
        }
        return "Agent: <color=orange>OFF NAVMESH</color>";
    }

    public static string FormatBusyReason(Character c)
    {
        if (c == null) return "Busy Reason: N/A";
        var reason = c.BusyReason;
        string color = reason == CharacterBusyReason.None ? "grey" : "#F5B027";
        return $"<color={color}>Busy Reason: {reason}</color>";
    }

    public static string FormatWorkPhaseGoap(Character c)
    {
        if (c == null) return "Phase: N/A\nGOAP Goal: N/A\nGOAP Action: N/A";

        string phase = "Phase: N/A";
        string goal = "GOAP Goal: None";
        string action = "GOAP Action: None";
        bool isLife = false;

        if (c.Controller is NPCController npc && npc.GoapController != null && npc.GoapController.CurrentAction != null)
        {
            isLife = true;
            string goalName = npc.GoapController.CurrentGoalName;
            goal = string.IsNullOrEmpty(goalName) || goalName == "None" ? "Life Goal: N/A" : $"Life Goal: {goalName}";
            action = $"Life Action: {npc.GoapController.CurrentAction.ActionName}";
            phase = "Phase: Life Routine";
        }
        else if (c.CharacterJob != null && c.CharacterJob.IsWorking && c.CharacterJob.CurrentJob != null)
        {
            string goalName = c.CharacterJob.CurrentJob.CurrentGoalName;
            goal = string.IsNullOrEmpty(goalName) ? "Job Goal: N/A" : $"Job Goal: {goalName}";
            string actionName = c.CharacterJob.CurrentJob.CurrentActionName;
            action = string.IsNullOrEmpty(actionName) ? "Job Action: N/A" : $"Job Action: {actionName}";
            var controller = c.Controller as NPCController;
            if (controller != null && controller.CurrentBehaviour != null && controller.CurrentBehaviour.GetType().Name == "WorkBehaviour")
                phase = "Phase: Working";
        }

        string color = isLife ? "#B0FFB0" : "#B0B0FF";
        return $"<color={color}>{phase}\n{goal}\n{action}</color>";
    }

    public static string FormatBt(Character c)
    {
        if (c == null) return "BT: N/A";
        if (c.Controller is NPCController npc)
        {
            if (npc.HasBehaviourTree) return "BT: " + npc.BehaviourTree.DebugCurrentNode;
            return "BT: N/A";
        }
        return "BT: N/A";
    }

    public static string FormatLifeGoap(Character c)
    {
        if (c == null) return "Life GOAP: N/A";
        if (c.Controller is NPCController npc && npc.GoapController != null && npc.GoapController.CurrentAction != null)
            return "Life GOAP: " + npc.GoapController.CurrentAction.ActionName;
        return "Life GOAP: None";
    }

    /// <summary>Composes every AI section into one multi-line string for a single TMP container.</summary>
    public static string FormatAll(Character c)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine(FormatAction(c));
        sb.AppendLine(FormatBehaviourStack(c));
        sb.AppendLine(FormatInteraction(c));
        sb.AppendLine(FormatAgent(c));
        sb.AppendLine(FormatBusyReason(c));
        sb.AppendLine(FormatWorkPhaseGoap(c));
        sb.AppendLine(FormatBt(c));
        sb.Append(FormatLifeGoap(c));
        return sb.ToString();
    }
}
