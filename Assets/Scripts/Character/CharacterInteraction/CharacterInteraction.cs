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
        _currentTarget = null;

        // Cet événement va prévenir TOUS ceux qui écoutent (dont l'UI) que c'est fini
        OnInteractionStateChanged?.Invoke(previousTarget, false);

        ResetBehaviourToDefault(_character);

        // Sécurité pour la cible
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

        // --- LA CORRECTION EST ICI ---
        // Si le comportement actuel est déjà "FollowTargetBehaviour", 
        // on ne veut SURTOUT PAS le remettre en Wander.
        if (controller.CurrentBehaviour is FollowTargetBehaviour)
        {
            Debug.Log($"<color=green>[Interaction]</color> {character.CharacterName} est en mode Follow, on ne reset pas son comportement.");
            return;
        }

        // Sinon, on remet le comportement par défaut
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

    /// <summary>
    /// Exécute une action d'interaction spécifique sur la cible actuelle.
    /// </summary>
    /// <param name="action">L'action à exécuter (ex: InteractionAskToFollow).</param>
    public void PerformInteraction(ICharacterInteractionAction action)
    {
        if (action == null)
        {
            Debug.LogWarning($"<color=red>[Interaction]</color> Tentative d'exécuter une action nulle sur {_character.CharacterName}");
            return;
        }

        if (_currentTarget == null)
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> {_character.CharacterName} essaie d'exécuter {action.GetType().Name} mais n'a pas de cible !");
            return;
        }

        Debug.Log($"<color=green>[Interaction]</color> {_character.CharacterName} exécute {action.GetType().Name} sur {_currentTarget.CharacterName}");

        // Exécution de l'interface
        action.Execute(_character, _currentTarget);
    }
}