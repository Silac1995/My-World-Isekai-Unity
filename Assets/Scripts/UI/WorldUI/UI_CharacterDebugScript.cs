using TMPro;
using UnityEngine;

public class UI_CharacterDebugScript : MonoBehaviour
{
    [SerializeField] private Character character;
    [SerializeField] private TextMeshProUGUI characterActionDebugText;
    [SerializeField] private TextMeshProUGUI characterBehaviourDebugText;
    [SerializeField] private TextMeshProUGUI characterInteractionDebugText; // Nouveau champ

    private void Update()
    {
        if (character == null) return;

        UpdateActionDebug();
        UpdateBehaviourDebug();
        UpdateInteractionDebug(); // Ajout du refresh de l'interaction
    }

    private void UpdateActionDebug()
    {
        if (characterActionDebugText == null) return;

        var currentAction = character.CharacterActions.CurrentAction;
        if (currentAction != null)
        {
            characterActionDebugText.text = $"Action: {currentAction.GetType().Name}";
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

        var controller = character.GetComponent<CharacterGameController>();
        if (controller != null && controller.CurrentBehaviour != null)
        {
            characterBehaviourDebugText.text = $"IA: {controller.CurrentBehaviour.GetType().Name}";
            characterBehaviourDebugText.color = Color.cyan;
        }
        else
        {
            characterBehaviourDebugText.text = "IA: None (Manual)";
            characterBehaviourDebugText.color = Color.gray;
        }
    }

    private void UpdateInteractionDebug()
    {
        if (characterInteractionDebugText == null) return;

        // On utilise la propriété CurrentTarget que l'on a créée dans CharacterInteraction
        var interaction = character.CharacterInteraction;

        if (interaction != null && interaction.IsInteracting)
        {
            // Affiche le nom du personnage cible
            characterInteractionDebugText.text = $"Interaction with: {interaction.CurrentTarget.CharacterName}";
            characterInteractionDebugText.color = Color.green; // Vert pour indiquer un lien actif
        }
        else
        {
            characterInteractionDebugText.text = "Interaction with: None";
            characterInteractionDebugText.color = Color.gray;
        }
    }
}