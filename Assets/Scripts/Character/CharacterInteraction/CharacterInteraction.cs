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
        // On ne vérifie la distance que s'ils ont déjà fini de se rapprocher
        if (IsInteracting && IsPositioned)
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
        
        // Si les personnages sont déjà positionnés (en plein dialogue), on est plus tolérant sur la distance
        // pour éviter que le NavMesh avoidance ou un micro-recul annule tout.
        if (IsPositioned)
        {
            float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
            if (dist < 3.5f) isStillTouching = true;
        }

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

        Debug.Log($"<color=cyan>[Interaction]</color> {_character.CharacterName} démarre le positionnement pour {target.CharacterName}.");

        // --- POSITIONNEMENT DE L'INITIATEUR ---
        if (_character.Controller is NPCController)
        {
            // Arrêter l'animation de marche immédiatement
            _character.CharacterVisual?.CharacterAnimator?.StopLocomotion();

            // Déplacement précis avec Coroutine : On se déplace vers la cible, puis ExecuteInteraction
            if (_activeDialogueCoroutine != null) StopCoroutine(_activeDialogueCoroutine);
            _activeDialogueCoroutine = StartCoroutine(MoveToInteractionRoutine(target, forcedFirstAction, onPositioned));
        }
        else
        {
            // Si pas de controller (ou joueur manuel), on lance direct le dialogue
            ExecuteInteraction(target, forcedFirstAction, onPositioned);
        }
    }

    private System.Collections.IEnumerator MoveToInteractionRoutine(Character target, ICharacterInteractionAction forcedFirstAction, Action onPositioned)
    {
        float timeoutTimer = 0f;
        const float TIMEOUT_DURATION = 5f;
        var movement = _character.CharacterMovement;
        var detector = _character.GetComponent<CharacterInteractionDetector>();
        var targetInteractable = target.CharacterInteractable;
        
        while (true)
        {
            if (target == null || !target.IsFree())
            {
                Debug.LogWarning($"<color=orange>[Interaction]</color> Cible perdue pendant le mouvement.");
                EndInteraction();
                yield break;
            }

            timeoutTimer += Time.deltaTime;
            if (timeoutTimer > TIMEOUT_DURATION)
            {
                Debug.LogWarning($"<color=orange>[Interaction]</color> Timeout de positionnement pour {_character.CharacterName} vers {target.CharacterName}. ABORT.");
                EndInteraction();
                yield break;
            }

            bool isCloseEnough = false;

            Vector3 targetPos = target.transform.position;
            // Offset de 2 unités sur l'axe X pour le face-à-face (ici 4)
            float xOffset = _character.transform.position.x > targetPos.x ? 4f : -4f;
            Vector3 desiredPos = new Vector3(targetPos.x + xOffset, _character.transform.position.y, targetPos.z);

            float distDelta = Vector3.Distance(new Vector3(_character.transform.position.x, 0, _character.transform.position.z), 
                                               new Vector3(desiredPos.x, 0, desiredPos.z));

            if (detector != null && targetInteractable != null)
            {
                bool isOverlapping = detector.IsOverlapping(targetInteractable);
                
                // Le joueur exige un alignement parfait : même Z, distance X de 4. Pas plus, pas moins.
                float zDiff = Mathf.Abs(_character.transform.position.z - targetPos.z);
                float xDiff = Mathf.Abs(_character.transform.position.x - targetPos.x);
                bool isAlignedVisually = zDiff <= 0.05f && Mathf.Abs(xDiff - 4f) <= 0.05f;

                isCloseEnough = (isOverlapping && isAlignedVisually) || distDelta <= 0.05f;
            }
            else
            {
                isCloseEnough = distDelta <= 0.05f;
            }

            if (isCloseEnough)
            {
                _character.CharacterVisual?.FaceCharacter(target);
                SetPositioned(true);
                if (movement != null) movement.Stop();
                
                ExecuteInteraction(target, forcedFirstAction, onPositioned);
                yield break;
            }

            SetPositioned(false);
            if (movement != null)
            {
                movement.Resume();
                movement.SetDestination(desiredPos);
            }

            yield return null;
        }
    }

    private void ExecuteInteraction(Character target, ICharacterInteractionAction forcedFirstAction, Action onPositioned)
    {
        if (target == null) return;
        
        // Double sécurité : L'un des deux a pu devenir occupé pendant le trajet !
        if (!_character.IsFree() || !target.IsFree())
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> {target.CharacterName} ou {_character.CharacterName} n'est plus libre après le trajet.");
            return;
        }

        CurrentTarget = target;
        IsPositioned = true; 
        OnInteractionStateChanged?.Invoke(target, true);

        // --- FACE-À-FACE IMMÉDIAT ---
        _character.CharacterVisual?.SetLookTarget(target);
        target.CharacterInteraction.SetInteractionTargetInternal(_character);

        // --- GESTION DE LA RELATION ---
        Relationship rel = _character.CharacterRelation.AddRelationship(target);
        if (rel != null) rel.SetAsMet();

        Relationship targetRel = target.CharacterRelation.GetRelationshipWith(_character);
        if (targetRel != null) targetRel.SetAsMet();

        // --- FREEZE DE LA CIBLE ET DE L'INITIATEUR ---
        if (target.Controller != null && !target.IsPlayer()) target.Controller.Freeze();
        if (_character.Controller != null) _character.Controller.Freeze();

        if (_activeDialogueCoroutine != null) StopCoroutine(_activeDialogueCoroutine);
        _activeDialogueCoroutine = StartCoroutine(DialogueSequence(_character, target, forcedFirstAction));
        
        onPositioned?.Invoke();
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

                // --- NEW: Wait if an invitation is pending (thinking/responding phase) ---
                if (currentListener.CharacterInvitation != null)
                {
                    // Give it a frame to register that it's pending if started by action.Execute
                    yield return null; 
                    while (currentListener.CharacterInvitation.HasPendingInvitation)
                    {
                        yield return new WaitForSeconds(0.2f);
                    }
                    
                    // If it was an invitation, we wait an extra bit for the response bubble to be readable
                    if (action is InteractionInvitation)
                    {
                        yield return new WaitForSeconds(2.0f);
                    }
                }
            }
            else if (currentSpeaker.IsPlayer())
            {
                // Si c'est le joueur, pour l'instant on fait juste un Talk basique par défaut 
                new InteractionTalk().Execute(currentSpeaker, currentListener);
            }

            totalExchanges++;
            if (totalExchanges >= MAX_EXCHANGES) break;

            // 2. Attente aléatoire avant la réponse (entre 2.5 et 5 secondes)
            // Mais on s'assure d'abord d'attendre a minima la fin de la bulle de texte
            while (currentSpeaker.CharacterSpeech != null && currentSpeaker.CharacterSpeech.IsSpeaking)
            {
                yield return null;
            }

            float randomDelay = UnityEngine.Random.Range(1.0f, 2.5f); // Délai réduit car on attend déjà la fin de la bulle
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

        // --- ATTENTE FINALE DE SÉCURITÉ ---
        // On s'assure que toutes les bulles de texte sont bien fermées avant de libérer les personnages
        while ((initiator.CharacterSpeech != null && initiator.CharacterSpeech.IsSpeaking) ||
               (target.CharacterSpeech != null && target.CharacterSpeech.IsSpeaking))
        {
            yield return null;
        }

        // --- DÉLAI DE FIN D'INTERACTION ---
        // Petit délai supplémentaire (ex: 2 secondes) pour que les personnages ne s'enfuient pas immédiatement
        yield return new WaitForSeconds(2.0f);

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

        // --- NEW: Clean up redundant movement plans towards the interlocutor ---
        ClearRedundantMovement(previousTarget);

        // --- NEW: Clear behaviours BEFORE unfreezing ---
        // This ensures the last command given to the movement system isn't 'Stop' 
        // after we've already tried to Resume via Unfreeze.
        
        // Target cleanup
        if (previousTarget.Controller is NPCController targetNpc)
        {
            if (previousTarget.GetComponent<NPCBehaviourTree>() != null)
            {
                targetNpc.ClearBehaviours();
            }
        }
        if (previousTarget.Controller != null) previousTarget.Controller.Unfreeze();

        // Initiator cleanup
        if (_character.Controller is NPCController initNpc)
        {
            if (_character.GetComponent<NPCBehaviourTree>() != null)
            {
                initNpc.ClearBehaviours();
            }
        }
        if (_character.Controller != null) _character.Controller.Unfreeze();

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

        // --- NEW: If we were already planning to move towards this target to talk, cancel it ---
        ClearRedundantMovement(target);

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

    private void ClearRedundantMovement(Character target)
    {
        if (_character.Controller == null || target == null) return;
        var npc = _character.Controller as NPCController;
        if (npc == null) return;
        
        // Native movement manages itself through the Coroutine and CharacterMovement natively.
    }
}
