using System;
using UnityEngine;

public class CharacterInteraction : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private Character _currentTarget;
    [SerializeField] private Collider _interactionZone;
    [SerializeField] private GameObject _interactionActionPrefab;

    public event Action<Character, bool> OnInteractionStateChanged;

    public Character CurrentTarget
    {
        get => _currentTarget;
        private set => _currentTarget = value;
    }

    public GameObject InteractionActionPrefab => _interactionActionPrefab;
    public bool IsInteracting => _currentTarget != null;

    private void Update()
    {
        // On ne vérifie que si on est en train d'interagir
        if (IsInteracting)
        {
            CheckInteractionDistance();
        }
    }

    private void CheckInteractionDistance()
    {
        if (_currentTarget == null || _interactionZone == null) return;

        // On récupère le collider de la cible
        Collider targetCollider = _currentTarget.CharacterInteraction._interactionZone;

        if (targetCollider == null) return;

        // Vérifie si les deux colliders s'intersectent toujours
        // Bounds.Intersects est très efficace pour ça
        bool isStillTouching = _interactionZone.bounds.Intersects(targetCollider.bounds);

        if (!isStillTouching)
        {
            Debug.Log($"<color=yellow>[Interaction]</color> Cible hors de zone, fin de l'interaction.");
            EndInteraction();
        }
    }

    public void StartInteractionWith(Character target)
    {
        if (target == null || _currentTarget == target) return;
        if (!_character.IsFree() || !target.IsFree()) return;

        CurrentTarget = target;
        OnInteractionStateChanged?.Invoke(target, true);

        target.CharacterInteraction.SetInteractionTargetInternal(_character);
    }

    public void EndInteraction()
    {
        if (_currentTarget == null) return;

        Character previousTarget = _currentTarget;
        CurrentTarget = null;

        OnInteractionStateChanged?.Invoke(previousTarget, false);

        ResetBehaviourToDefault(_character);

        // Sécurité pour éviter une boucle infinie de EndInteraction
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
        // On déclenche aussi l'event pour le partenaire afin que ses oreilles bougent aussi !
        OnInteractionStateChanged?.Invoke(target, true);
    }
}