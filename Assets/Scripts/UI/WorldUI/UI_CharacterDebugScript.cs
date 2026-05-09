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
    [SerializeField] private TextMeshProUGUI workPhaseGOAPText;
    [SerializeField] private TextMeshProUGUI btStateText;
    [SerializeField] private TextMeshProUGUI lifeGoapStateText;

    private void Update()
    {
        if (character == null) return;

        if (characterActionDebugText != null) characterActionDebugText.text = CharacterAIDebugFormatter.FormatAction(character);
        if (characterBehaviourDebugText != null) characterBehaviourDebugText.text = CharacterAIDebugFormatter.FormatBehaviourStack(character);
        if (characterInteractionDebugText != null) characterInteractionDebugText.text = CharacterAIDebugFormatter.FormatInteraction(character);
        if (agentState != null) agentState.text = CharacterAIDebugFormatter.FormatAgent(character);
        if (busyReasonText != null) busyReasonText.text = CharacterAIDebugFormatter.FormatBusyReason(character);
        if (workPhaseGOAPText != null) workPhaseGOAPText.text = CharacterAIDebugFormatter.FormatWorkPhaseGoap(character);
        if (btStateText != null) btStateText.text = CharacterAIDebugFormatter.FormatBt(character);
        if (lifeGoapStateText != null) lifeGoapStateText.text = CharacterAIDebugFormatter.FormatLifeGoap(character);
        if (characterNeedsText != null) characterNeedsText.text = FormatNeeds(character);
    }

    // Kept local — this one isn't reused by the inspector (NeedsSubTab formats its own).
    private static string FormatNeeds(Character character)
    {
        var needsSystem = character.CharacterNeeds;
        if (needsSystem == null) return "<color=grey>Needs: N/A</color>";

        var needs = needsSystem.AllNeeds;
        if (needs == null || needs.Count == 0) return "<color=grey>Needs: None registered</color>";

        var sb = new System.Text.StringBuilder(256);
        sb.Append("Besoins:");
        foreach (var need in needs)
        {
            float urgency = need.GetUrgency();
            bool isActive = need.IsActive();
            string colorCode = !isActive ? "#888888" : (urgency >= 100 ? "#FF4444" : "#F5B027");
            string status = isActive ? "ON" : "OFF";
            sb.Append($"\n<color={colorCode}>  {need.GetType().Name}: {urgency:F0}% [{status}]</color>");
        }
        return sb.ToString();
    }
}
