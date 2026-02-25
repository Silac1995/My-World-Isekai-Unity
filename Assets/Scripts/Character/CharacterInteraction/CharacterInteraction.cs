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
    private Coroutine _activeDialogueCoroutine;

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

    public void StartInteractionWith(Character target, ICharacterInteractionAction forcedFirstAction = null, Action onPositioned = null)
    {
        if (target == null || _currentTarget == target) return;
        
        // --- SÉCURITÉ : Personnages occupés ou incapacités ---
        if (!_character.IsFree())
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> {_character.CharacterName} n'est pas libre pour démarrer une interaction.");
            return;
        }
        if (!target.IsFree())
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> {target.CharacterName} n'est pas libre pour recevoir une interaction.");
            return;
        }

        CurrentTarget = target;
        IsPositioned = false; 
        OnInteractionStateChanged?.Invoke(target, true);

        // --- FACE-À-FACE IMMÉDIAT ---
        _character.CharacterVisual?.SetLookTarget(target);

        target.CharacterInteraction.SetInteractionTargetInternal(_character);

        // --- GESTION DE LA RELATION ---
        Relationship rel = _character.CharacterRelation.AddRelationship(target);
        if (rel != null) rel.SetAsMet();

        Relationship targetRel = target.CharacterRelation.GetRelationshipWith(_character);
        if (targetRel != null) targetRel.SetAsMet();

        // --- FREEZE DE LA CIBLE (pas le joueur) ---
        if (target.Controller != null && !target.IsPlayer())
        {
            target.Controller.Freeze();
        }

        // --- POSITIONNEMENT DE L'INITIATEUR ---
        if (_character.Controller != null)
        {
            // Arrêter l'animation de marche immédiatement
            _character.CharacterVisual?.CharacterAnimator?.StopLocomotion();

            // Déplacement précis avec callback : On freeze + lance le dialogue UNE FOIS en place
            _character.Controller.PushBehaviour(new MoveToInteractionBehaviour(_character.Controller, target, () => 
            {
                _character.Controller.Freeze();
                if (_activeDialogueCoroutine != null) StopCoroutine(_activeDialogueCoroutine);
                _activeDialogueCoroutine = StartCoroutine(DialogueSequence(_character, target, forcedFirstAction));
                onPositioned?.Invoke();
            }));
        }
        else
        {
            // Si pas de controller, on lance direct le dialogue
            IsPositioned = true;
            if (_activeDialogueCoroutine != null) StopCoroutine(_activeDialogueCoroutine);
            _activeDialogueCoroutine = StartCoroutine(DialogueSequence(_character, target, forcedFirstAction));
            onPositioned?.Invoke();
        }

        Debug.Log($"<color=cyan>[Interaction]</color> {_character.CharacterName} démarre le positionnement pour {target.CharacterName}.");
    }

    private System.Collections.IEnumerator DialogueSequence(Character initiator, Character target, ICharacterInteractionAction forcedFirstAction = null)
    {
        int totalExchanges = 0;
        const int MAX_EXCHANGES = 6;

        Character currentSpeaker = initiator;
        Character currentListener = target;

        while (totalExchanges < MAX_EXCHANGES)
        {
            // 1. L'orateur actuel effectue une action
            if (forcedFirstAction != null && totalExchanges == 0)
            {
                forcedFirstAction.Execute(currentSpeaker, currentListener);
            }
            else if (currentSpeaker.Controller is NPCController npc)
            {
                var action = npc.GetRandomSocialAction(currentListener);
                action.Execute(currentSpeaker, currentListener);
            }
            else if (currentSpeaker.IsPlayer())
            {
                // Si c'est le joueur, pour l'instant on fait juste un Talk basique par défaut 
                new InteractionTalk().Execute(currentSpeaker, currentListener);
            }

            totalExchanges++;
            if (totalExchanges >= MAX_EXCHANGES) break;

            // 2. Attente aléatoire avant la réponse (entre 2.5 et 5 secondes)
            float randomDelay = UnityEngine.Random.Range(2.5f, 5.0f);
            yield return new WaitForSeconds(randomDelay);

            // 3. Inversion des rôles pour le tour suivant
            Character nextSpeaker = currentListener;
            Character nextListener = currentSpeaker;

            // 4. Est-ce que le prochain orateur souhaite répondre ?
            if (nextSpeaker.Controller is NPCController nextNpc)
            {
                if (!nextNpc.ShouldRespondTo(nextListener))
                {
                    Debug.Log($"<color=orange>[Dialogue]</color> {nextSpeaker.CharacterName} met fin à la conversation (ne souhaite pas répondre).");
                    break;
                }
            }
            else if (nextSpeaker.IsPlayer())
            {
                // Le joueur ne refuse jamais de répondre automatiquement dans ce prototype simple
            }

            currentSpeaker = nextSpeaker;
            currentListener = nextListener;
        }

        Debug.Log($"<color=cyan>[Dialogue]</color> Fin de la séquence après {totalExchanges} échanges.");
        EndInteraction();
    }

    public void EndInteraction()
    {
        if (_currentTarget == null) return;

        Character previousTarget = _currentTarget;
        _currentTarget = null;
        IsPositioned = false;

        // Libérer le regard
        _character.CharacterVisual?.ClearLookTarget();

        if (_activeDialogueCoroutine != null)
        {
            StopCoroutine(_activeDialogueCoroutine);
            _activeDialogueCoroutine = null;
        }

        // On unfreeze la cible
        if (previousTarget.Controller != null)
        {
            previousTarget.Controller.Unfreeze();
        }

        // On unfreeze l'initiateur
        if (_character.Controller != null)
        {
            // Nettoyer le MoveToInteraction s'il est encore dans la pile
            if (_character.Controller.CurrentBehaviour is MoveToInteractionBehaviour)
                _character.Controller.PopBehaviour();

            _character.Controller.Unfreeze();
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
        if (!_character.IsFree()) return; // Fail-safe supplémentaire

        CurrentTarget = target;
        IsPositioned = true; // La cible est passivement prête
        
        // --- FACE-À-FACE IMMÉDIAT ---
        _character.CharacterVisual?.SetLookTarget(target);

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
