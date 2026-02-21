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
    
    // --- POSITIONNEMENT ---
    public bool IsPositioned { get; private set; }
    public void SetPositioned(bool value) => IsPositioned = value;

    private void Update()
    {
        if (IsInteracting)
        {
            CheckInteractionDistance();
        }
    }

    private void CheckInteractionDistance()
    {
        if (_currentTarget == null || _interactionZone == null) return;
        Collider targetCollider = _currentTarget.CharacterInteraction._interactionZone;
        if (targetCollider == null) return;

        bool isStillTouching = _interactionZone.bounds.Intersects(targetCollider.bounds);
        if (!isStillTouching)
        {
            Debug.Log($"<color=yellow>[Interaction]</color> Cible hors de zone, fin de l'interaction.");
            EndInteraction();
        }
    }

    public void StartInteractionWith(Character target, Action onPositioned = null)
    {
        if (target == null || _currentTarget == target) return;
        if (!_character.IsFree() || !target.IsFree()) return;

        CurrentTarget = target;
        IsPositioned = false; 
        OnInteractionStateChanged?.Invoke(target, true);

        // --- FACE-À-FACE IMMÉDIAT ---
        _character.CharacterVisual?.FaceCharacter(target);

        target.CharacterInteraction.SetInteractionTargetInternal(_character);

        // --- GESTION DE LA RELATION ---
        Relationship rel = _character.CharacterRelation.AddRelationship(target);
        if (rel != null) rel.SetAsMet();

        Relationship targetRel = target.CharacterRelation.GetRelationshipWith(_character);
        if (targetRel != null) targetRel.SetAsMet();

        // --- FREEZE DE LA CIBLE (si NPC) ---
        if (target.Controller != null && target.Controller is NPCController)
        {
            target.Controller.PushBehaviour(new InteractBehaviour());
        }

        // --- POSITIONNEMENT DE L'INITIATEUR ---
        if (_character.Controller != null)
        {
            // Pause de base
            _character.Controller.PushBehaviour(new InteractBehaviour());
            // Déplacement précis avec callback
            _character.Controller.PushBehaviour(new MoveToInteractionBehaviour(_character.Controller, target, onPositioned));
        }
        else
        {
            // Si pas de controller (ex: script externe forçant l'interaction), on considère comme positionné
            IsPositioned = true;
            onPositioned?.Invoke();
        }

        Debug.Log($"<color=cyan>[Interaction]</color> {_character.CharacterName} démarre le positionnement pour {target.CharacterName}.");
    }

    public void EndInteraction()
    {
        if (_currentTarget == null) return;

        Character previousTarget = _currentTarget;
        _currentTarget = null;
        IsPositioned = false;

        // On libère la cible si elle était freezée
        if (previousTarget.Controller != null)
        {
            if (previousTarget.Controller.CurrentBehaviour is InteractBehaviour)
                previousTarget.Controller.PopBehaviour();
        }

        // On libère l'initiateur
        if (_character.Controller != null)
        {
            // On nettoie la pile des comportements d'interaction
            if (_character.Controller.CurrentBehaviour is MoveToInteractionBehaviour)
                _character.Controller.PopBehaviour();
            
            if (_character.Controller.CurrentBehaviour is InteractBehaviour)
                _character.Controller.PopBehaviour();
        }

        _character.CharacterInteractable?.Release();
        OnInteractionStateChanged?.Invoke(previousTarget, false);

        if (previousTarget.CharacterInteraction.CurrentTarget == _character)
        {
            previousTarget.CharacterInteraction.EndInteraction();
        }
    }

    internal void SetInteractionTargetInternal(Character target)
    {
        CurrentTarget = target;
        IsPositioned = true; // La cible est passivement prête
        
        // --- FACE-À-FACE IMMÉDIAT ---
        _character.CharacterVisual?.FaceCharacter(target);

        OnInteractionStateChanged?.Invoke(target, true);
    }

    public void PerformInteraction(ICharacterInteractionAction action)
    {
        if (action == null) return;

        if (_currentTarget == null) return;

        if (!IsPositioned)
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> {_character.CharacterName} n'est pas encore en place (attendu 10f en X et aligné Z).");
            return;
        }

        Debug.Log($"<color=green>[Interaction]</color> {_character.CharacterName} exécute {action.GetType().Name} sur {_currentTarget.CharacterName}");
        action.Execute(_character, _currentTarget);
    }
}
