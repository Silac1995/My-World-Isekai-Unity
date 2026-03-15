using System.Linq;
using TMPro;
using UnityEngine;

public class UI_CharacterDebugScript : MonoBehaviour
{
    [SerializeField] private Character character;
    [SerializeField] private TextMeshProUGUI characterActionDebugText;
    [SerializeField] private TextMeshProUGUI characterBehaviourDebugText;
    [SerializeField] private TextMeshProUGUI characterInteractionDebugText;
    [SerializeField] private TextMeshProUGUI characterNeedsText;
    [SerializeField] private TextMeshProUGUI agentState;
    [SerializeField] private TextMeshProUGUI busyReasonText;
    [SerializeField] private TextMeshProUGUI workPhaseGOAPText; // Add this specific field on the prefab later
    [SerializeField] private TextMeshProUGUI btStateText;
    [SerializeField] private TextMeshProUGUI lifeGoapStateText;

    private void Update()
    {
        if (character == null) return;

        UpdateActionDebug();
        UpdateBehaviourDebug();
        UpdateInteractionDebug();
        UpdateNeedsDebug(); // Ajout de l'affichage des besoins
        UpdateAgentDebug();
        UpdateBusyReasonDebug();
        UpdateWorkPhaseGOAPDebug();
        UpdateBTAndLifeGOAPDebug();
    }

    private void UpdateActionDebug()
    {
        if (characterActionDebugText == null) return;

        var currentAction = character.CharacterActions.CurrentAction;
        if (currentAction != null)
        {
            characterActionDebugText.text = "Action: " + currentAction.GetType().Name;
            characterActionDebugText.color = Color.yellow;
        }
        else
        {
            characterActionDebugText.text = "Action: Idle";
            characterActionDebugText.color = Color.white;
        }
    }

    private void UpdateBehaviourDebug()
    {
        if (characterBehaviourDebugText == null) return;

        var controller = character.Controller as NPCController;
        if (controller != null)
        {
            var stackNames = controller.GetBehaviourStackNames();

            if (stackNames.Any())
            {
                // Le premier est toujours le Current (sommet de la pile)
                string current = "<color=#00FFFF>Current: " + stackNames.First() + "</color>";

                // Les suivants sont en attente
                string next = "";
                if (stackNames.Skip(1).Any())
                {
                    next = "\n<color=#F5B027>Queue: " + string.Join(" -> ", stackNames.Skip(1)) + "</color>";
                }

                characterBehaviourDebugText.text = current + next;
            }
            else
            {
                characterBehaviourDebugText.text = "IA: Empty Stack";
                characterBehaviourDebugText.color = Color.gray;
            }
        }
    }

    private void UpdateInteractionDebug()
    {
        if (characterInteractionDebugText == null) return;

        // On utilise la propriete CurrentTarget que l'on a cree dans CharacterInteraction
        var interaction = character.CharacterInteraction;

        if (interaction != null && interaction.IsInteracting)
        {
            // Affiche le nom du personnage cible
            characterInteractionDebugText.text = "Interaction with: " + interaction.CurrentTarget.CharacterName;
            characterInteractionDebugText.color = Color.green; // Vert pour indiquer un lien actif
        }
        else
        {
            characterInteractionDebugText.text = "Interaction with: None";
            characterInteractionDebugText.color = Color.gray;
        }
    }

    private void UpdateNeedsDebug()
    {
        if (characterNeedsText == null) return;

        // Utilisation de la reference directe depuis la classe Character
        var needsSystem = character.CharacterNeeds;

        if (needsSystem != null)
        {
            var needs = needsSystem.AllNeeds;
            if (needs == null || needs.Count == 0)
            {
                characterNeedsText.text = "Needs: None registered";
                characterNeedsText.color = Color.gray;
                return;
            }

            string debugContent = "Besoins:";
            foreach (var need in needs)
            {
                float urgency = need.GetUrgency();
                bool isActive = need.IsActive();

                // Formatage des couleurs : 
                // Gris = Inactif / Jaune = Actif / Rouge = Urgent (>=100)
                string colorCode = !isActive ? "#888888" : (urgency >= 100 ? "#FF4444" : "#F5B027");

                string status = isActive ? "ON" : "OFF";
                debugContent += "\n<color=" + colorCode + ">  " + need.GetType().Name + ": " + urgency.ToString("F0") + "% [" + status + "]</color>";
            }

            characterNeedsText.text = debugContent;
        }
        else
        {
            characterNeedsText.text = "Needs: N/A";
            characterNeedsText.color = Color.gray;
        }
    }

    // La methode a ajouter
    private void UpdateAgentDebug()
    {
        if (agentState == null) return;

        // Si c'est le joueur, on affiche un etat special
        if (character.IsPlayer())
        {
            agentState.text = "Agent: <color=grey>PLAYER (Manual)</color>";
            return;
        }

        var controller = character.GetComponent<CharacterGameController>();
        if (controller != null && controller.Agent != null && controller.Agent.isOnNavMesh)
        {
            var agent = controller.Agent;
            string stoppedStatus = agent.isStopped ? "<color=red>STOPPED</color>" : "<color=green>RUNNING</color>";
            string pathStatus = agent.hasPath ? "Has Path" : "No Path";
            agentState.text = "Agent: " + stoppedStatus + " | " + pathStatus;
        }
        else
        {
            agentState.text = "Agent: <color=orange>OFF NAVMESH</color>";
        }
    }

    private void UpdateBusyReasonDebug()
    {
        if (busyReasonText == null) return;

        var reason = character.BusyReason;
        if (reason == CharacterBusyReason.None)
        {
            busyReasonText.text = "Busy Reason: " + reason;
            busyReasonText.color = Color.gray;
        }
        else
        {
            busyReasonText.text = "Busy Reason: " + reason;
            busyReasonText.color = Color.yellow;
        }
    }

    private void UpdateWorkPhaseGOAPDebug()
    {
        if (workPhaseGOAPText == null) return;
        
        string goapGoalText = "GOAP Goal: None";
        string goapActionText = "GOAP Action: None";
        string phaseText = "Phase: N/A";

        bool isLifeGoapActive = false;
        if (character.Controller is NPCController npc && npc.GoapController != null && npc.GoapController.CurrentAction != null)
        {
            isLifeGoapActive = true;
            string goalName = npc.GoapController.CurrentGoalName;
            goapGoalText = string.IsNullOrEmpty(goalName) || goalName == "None" ? "Life Goal: N/A" : $"Life Goal: {goalName}";

            string actionName = npc.GoapController.CurrentAction.ActionName;
            goapActionText = $"Life Action: {actionName}";
            phaseText = "Phase: Life Routine";
        }
        else if (character != null && character.CharacterJob != null && character.CharacterJob.IsWorking && character.CharacterJob.CurrentJob != null)
        {
            string goalName = character.CharacterJob.CurrentJob.CurrentGoalName;
            goapGoalText = string.IsNullOrEmpty(goalName) ? "Job Goal: N/A" : $"Job Goal: {goalName}";

            string actionName = character.CharacterJob.CurrentJob.CurrentActionName;
            goapActionText = string.IsNullOrEmpty(actionName) ? "Job Action: N/A" : $"Job Action: {actionName}";
            
            var controller = character.Controller as NPCController;
            if (controller != null && controller.CurrentBehaviour != null && controller.CurrentBehaviour.GetType().Name == "WorkBehaviour")
            {
                phaseText = "Phase: Working";
            }
        }

        workPhaseGOAPText.text = $"{phaseText}\n{goapGoalText}\n{goapActionText}";
        workPhaseGOAPText.color = isLifeGoapActive ? new Color(0.7f, 1f, 0.7f) : new Color(0.7f, 0.7f, 1f); 
    }

    private void UpdateBTAndLifeGOAPDebug()
    {
        if (character.Controller is NPCController npc)
        {
            if (btStateText != null)
            {
                if (npc.HasBehaviourTree)
                    btStateText.text = "BT: " + npc.BehaviourTree.DebugCurrentNode;
                else
                    btStateText.text = "BT: N/A";
            }

            if (lifeGoapStateText != null)
            {
                if (npc.GoapController != null && npc.GoapController.CurrentAction != null)
                    lifeGoapStateText.text = "Life GOAP: " + npc.GoapController.CurrentAction.ActionName;
                else
                    lifeGoapStateText.text = "Life GOAP: None";
            }
        }
        else
        {
            if (btStateText != null) btStateText.text = "BT: N/A";
            if (lifeGoapStateText != null) lifeGoapStateText.text = "Life GOAP: N/A";
        }
    }
}
