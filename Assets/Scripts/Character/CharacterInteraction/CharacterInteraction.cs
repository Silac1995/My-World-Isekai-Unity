using System;
using UnityEngine;

public class CharacterInteraction : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private Character _currentTarget;

    // Définition de l'évenement
    // On passe le partenaire de l'interaction (Target) et un booléen (IsStarting)
    public event Action<Character, bool> OnInteractionStateChanged;

    public Character CurrentTarget
    {
        get => _currentTarget;
        private set => _currentTarget = value;
    }

    public bool IsInteracting => _currentTarget != null;

    public void StartInteractionWith(Character target)
    {
        if (target == null || _currentTarget == target) return;

        if (!_character.IsFree() || !target.IsFree()) return;

        CurrentTarget = target;

        // Déclenchement de l'événement (true = début)
        OnInteractionStateChanged?.Invoke(target, true);

        target.CharacterInteraction.SetInteractionTargetInternal(_character);
    }

    public void EndInteraction()
    {
        if (_currentTarget == null) return;

        Character previousTarget = _currentTarget;
        CurrentTarget = null;

        // Déclenchement de l'événement (false = fin)
        OnInteractionStateChanged?.Invoke(previousTarget, false);

        ResetBehaviourToDefault(_character);

        if (previousTarget.CharacterInteraction.CurrentTarget == _character)
        {
            previousTarget.CharacterInteraction.EndInteraction();
        }
    }

    private void ResetBehaviourToDefault(Character character)
    {
        character.CharacterActions.ClearCurrentAction();

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

    internal void SetInteractionTargetInternal(Character target)
    {
        CurrentTarget = target;
    }
}