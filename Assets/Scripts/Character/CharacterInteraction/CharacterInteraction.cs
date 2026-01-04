using System;
using UnityEngine;

public class CharacterInteraction
{
    private readonly Character characterInitiator;

    // Propriété publique pour savoir avec qui on interagit
    public Character CurrentTarget { get; private set; }

    // Raccourci pour vérifier l'état
    public bool IsInteracting => CurrentTarget != null;

    public CharacterInteraction(Character character)
    {
        this.characterInitiator = character;
    }

    public void StartInteractionWith(Character target)
    {
        if (target == null || CurrentTarget == target) return;

        // On vérifie que l'initiateur et la cible sont libres
        if (!characterInitiator.IsFree() || !target.IsFree())
        {
            Debug.LogWarning($"{characterInitiator.CharacterName} ou {target.CharacterName} est occupé.");
            return;
        }

        // Établissement de la connexion
        CurrentTarget = target;

        // On définit la cible chez l'autre sans logique complexe pour éviter la récursion
        target.CharacterInteraction.SetInteractionTargetInternal(characterInitiator);

        Debug.Log($"{characterInitiator.CharacterName} (Initiator) a commencé une interaction avec {target.CharacterName}.");
    }

    public void PerformInteraction(ICharacterInteractionAction action, Character target)
    {
        if (action == null || target == null) return;

        StartInteractionWith(target);

        // On n'exécute l'action que si l'interaction a bien été établie
        if (CurrentTarget == target)
        {
            action.Execute(characterInitiator, target);
        }
    }

    public void EndInteraction()
    {
        if (CurrentTarget == null) return;

        Character previousTarget = CurrentTarget;
        CurrentTarget = null;

        // --- Logique de remise en état (Behaviour) ---
        // On nettoie le comportement du personnage local (celui qui possède ce script)
        ResetBehaviourToDefault(characterInitiator);

        Debug.Log($"{characterInitiator.CharacterName} a terminé l'interaction.");

        // --- Synchronisation avec l'autre personnage ---
        if (previousTarget.CharacterInteraction.CurrentTarget == characterInitiator)
        {
            previousTarget.CharacterInteraction.EndInteraction();
        }
    }

    private void ResetBehaviourToDefault(Character character)
    {
        // 1. On nettoie l'action en cours
        character.CharacterActions.ClearCurrentAction();

        // 2. On remet le comportement par défaut
        var controller = character.GetComponent<CharacterGameController>();
        if (controller == null) return;

        if (character.TryGetComponent<NPCController>(out var npc))
        {
            controller.SetBehaviour(new WanderBehaviour(npc));
        }
        else
        {
            controller.SetBehaviour(new IdleBehaviour());
        }
    }

    // Utilisé pour lier l'interaction du côté de la cible
    internal void SetInteractionTargetInternal(Character target)
    {
        CurrentTarget = target;
    }

    internal void PerformInteraction(InteractBehaviour interactBehaviour, Character character)
    {
        throw new NotImplementedException();
    }
}