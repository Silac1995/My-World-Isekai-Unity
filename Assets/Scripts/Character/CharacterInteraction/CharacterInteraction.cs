using System;
using UnityEngine;

public class CharacterInteraction : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private Character _currentTarget;
    [SerializeField] private Collider _interactionZone;
    [SerializeField] private GameObject _interactionActionPrefab;

    public Collider InteractionZone => _interactionZone;

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

        // --- GESTION DE LA RELATION ET DE LA RENCONTRE ---

        // 1. On s'assure que la relation existe (AddRelationship gère déjà la réciprocité)
        Relationship rel = _character.CharacterRelation.AddRelationship(target);

        // 2. On passe le statut à "Met" (Rencontré) pour le personnage actuel
        if (rel != null)
        {
            rel.SetAsMet();
        }

        // 3. On fait de même pour le partenaire (réciprocité du "HasMet")
        Relationship targetRel = target.CharacterRelation.GetRelationshipWith(_character);
        if (targetRel != null)
        {
            targetRel.SetAsMet();
        }

        Debug.Log($"<color=cyan>[Relation]</color> {_character.CharacterName} et {target.CharacterName} se sont officiellement rencontrés.");
    }

    public void EndInteraction()
    {
        // 1. Condition de sortie immédiate : si déjà nul, on ne fait rien
        if (_currentTarget == null) return;

        Debug.Log($"<color=yellow>[Interaction]</color> Fin de l'interaction entre {_character.CharacterName} et {_currentTarget.CharacterName}");

        // 2. Sauvegarde de la référence et nettoyage immédiat
        Character previousTarget = _currentTarget;
        _currentTarget = null; // IMPORTANT : On met à null AVANT d'appeler quoi que ce soit d'autre

        // 3. Libération du flag "Busy" du personnage local
        if (_character.CharacterInteractable != null)
        {
            _character.CharacterInteractable.Release();
        }

        // 4. Notification pour l'UI et les autres systèmes
        OnInteractionStateChanged?.Invoke(previousTarget, false);

        // 5. Reset du comportement (Wander/Idle)
        ResetBehaviourToDefault(_character);

        // 6. NETTOYAGE DE LA CIBLE (Réciprocité sécurisée)
        // On vérifie si la cible nous pointe encore. 
        // Comme on a mis NOTRE _currentTarget à null, l'appel suivant s'arrêtera à l'étape 1.
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