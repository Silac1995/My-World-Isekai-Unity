using System;
using Unity.Netcode;
using UnityEngine;

public class CharacterInteraction : CharacterSystem
{
    [SerializeField] private Character _currentTarget;
    [SerializeField] private Collider _interactionZone;
    [SerializeField] private GameObject _interactionActionPrefab;

    public Collider InteractionZone => _interactionZone;
    public event Action<Character, bool> OnInteractionStateChanged;
    public event Action<Character> OnPlayerTurnStarted;
    public event Action<Character> OnPlayerTurnEnded;
    /// <summary>
    /// Fired every frame during the player's turn with a normalized timer value (1.0 → 0.0).
    /// </summary>
    public event Action<float> OnPlayerTurnTimerUpdated;

    // Internal helpers so DialogueSequence can fire events on the correct CharacterInteraction
    // (the currentSpeaker's, not necessarily the initiator's).
    internal void NotifyPlayerTurnStarted(Character listener) => OnPlayerTurnStarted?.Invoke(listener);
    internal void NotifyPlayerTurnEnded(Character listener) => OnPlayerTurnEnded?.Invoke(listener);
    internal void NotifyPlayerTurnTimerUpdated(float value) => OnPlayerTurnTimerUpdated?.Invoke(value);
    private Coroutine _activeDialogueCoroutine;
    private ICharacterInteractionAction _playerPendingAction;
    
    // Suivi de la cible actuelle d'approche pour le désabonnement
    private Character _pendingTarget;

    public Character CurrentTarget
    {
        get => _currentTarget;
        private set => _currentTarget = value;
    }

    public GameObject InteractionActionPrefab => _interactionActionPrefab;
    public bool IsInteracting => _currentTarget != null;
    public bool IsInteractionProcessActive => _activeDialogueCoroutine != null || IsInteracting;
    
    // --- POSITIONNEMENT ---
    public bool IsPositioned { get; private set; }
    public bool IsPositioning { get; private set; } // Nouveau: Indique qu'on se met en place pour interagir
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

    public bool StartInteractionWith(Character target, ICharacterInteractionAction forcedFirstAction = null, Action onPositioned = null)
    {
        if (target == null || _currentTarget == target) return false;
        
        if (IsOwner && !IsServer)
        {
            ulong targetId = target.NetworkObject != null ? target.NetworkObject.NetworkObjectId : 0;
            string actionType = forcedFirstAction != null ? forcedFirstAction.GetType().AssemblyQualifiedName : "";
            RequestStartInteractionServerRpc(targetId, actionType);
            return true;
        }

        if (!IsServer && !IsOwner) return false; // Non-authoritative entity cannot dictate interactions

        // --- SÉCURITÉ : Personnages occupés ou incapacités ---
        if (!_character.IsFree())
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> {_character.CharacterName} n'est pas libre pour démarrer une interaction.");
            return false;
        }
        if (!target.IsFree())
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> {target.CharacterName} n'est pas libre pour recevoir une interaction.");
            return false;
        }

        Debug.Log($"<color=cyan>[Interaction]</color> {_character.CharacterName} démarre le positionnement pour {target.CharacterName}.");

        // --- POSITIONNEMENT DE L'INITIATEUR ---
        if (_character.Controller is NPCController)
        {
            // Arrêter l'animation de marche immédiatement
            _character.CharacterVisual?.CharacterAnimator?.StopLocomotion();
            
            if (_character.Controller != null) _character.Controller.Freeze();

            // --- NOUVEAU: S'abonner à l'état de la cible pour annuler si elle devient occupée ---
            if (_pendingTarget != null)
            {
                _pendingTarget.CharacterInteraction.OnInteractionStateChanged -= HandleTargetStateChanged;
            }
            _pendingTarget = target;
            _pendingTarget.CharacterInteraction.OnInteractionStateChanged += HandleTargetStateChanged;

            // Déplacement précis avec Coroutine : On se déplace vers la cible, puis ExecuteInteraction
            if (_activeDialogueCoroutine != null) StopCoroutine(_activeDialogueCoroutine);
            _activeDialogueCoroutine = StartCoroutine(MoveToInteractionRoutine(target, forcedFirstAction, onPositioned));
        }
        else
        {
            // Si pas de controller (ou joueur manuel), on lance direct le dialogue
            ExecuteInteraction(target, forcedFirstAction, onPositioned);
        }
        return true;
    }

    [Rpc(SendTo.Server)]
    private void RequestStartInteractionServerRpc(ulong targetId, string forcedActionType)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var netObj))
        {
            Character target = netObj.GetComponent<Character>();
            
            ICharacterInteractionAction action = null;
            if (!string.IsNullOrEmpty(forcedActionType))
            {
                Type type = Type.GetType(forcedActionType);
                if (type != null) action = (ICharacterInteractionAction)Activator.CreateInstance(type);
            }
            StartInteractionWith(target, action, null);
        }
    }

    [Rpc(SendTo.Server)]
    internal void RequestInvitationServerRpc(ulong targetId, string invitationType)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var netObj))
        {
            Character target = netObj.GetComponent<Character>();
            
            if (target != null && !string.IsNullOrEmpty(invitationType))
            {
                Type type = Type.GetType(invitationType);
                if (type != null) 
                {
                    var invitation = (InteractionInvitation)Activator.CreateInstance(type);
                    if (invitation.CanExecute(_character, target))
                    {
                        invitation.Execute(_character, target);
                    }
                }
            }
        }
    }

    private void CalculateValidMeetingPositions(Character initiator, Character target, out Vector3 initiatorMeetingPos, out Vector3 targetMeetingPos)
    {
        Vector3 initPos = initiator.transform.position;
        Vector3 targetPos = target.transform.position;
        bool initiatorIsOnLeft = initPos.x < targetPos.x;
        
        float[] offsetsToTest = new float[] { 2f, 2.5f, 3f }; // Total distances: 4f, 5f, 6f

        Vector3[] midpointsToTest = new Vector3[]
        {
            Vector3.Lerp(initPos, targetPos, 0.5f), // Center
            targetPos, // Shifted towards target
            initPos, // Shifted towards initiator
            Vector3.Lerp(initPos, targetPos, 0.25f),
            Vector3.Lerp(initPos, targetPos, 0.75f)
        };

        foreach(var offset in offsetsToTest)
        {
            foreach(var mid in midpointsToTest)
            {
                Vector3 testInit = initiatorIsOnLeft ? new Vector3(mid.x - offset, initPos.y, mid.z) : new Vector3(mid.x + offset, initPos.y, mid.z);
                Vector3 testTarget = initiatorIsOnLeft ? new Vector3(mid.x + offset, targetPos.y, mid.z) : new Vector3(mid.x - offset, targetPos.y, mid.z);

                // Test if both points are valid on NavMesh
                if (UnityEngine.AI.NavMesh.SamplePosition(testInit, out UnityEngine.AI.NavMeshHit initHit, 1.0f, UnityEngine.AI.NavMesh.AllAreas) &&
                    UnityEngine.AI.NavMesh.SamplePosition(testTarget, out UnityEngine.AI.NavMeshHit targetHit, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    float xDist = Mathf.Abs(initHit.position.x - targetHit.position.x);
                    // Verify they maintain perfect Z alignment and acceptable X distance (between ~3.5f and ~6.5f)
                    if (Mathf.Abs(initHit.position.z - targetHit.position.z) < 0.2f && xDist >= 3.5f && xDist <= 6.5f)
                    {
                        // Force exact Z alignment and rigid spacing based on the best fit
                        initiatorMeetingPos = new Vector3(initHit.position.x, initPos.y, initHit.position.z);
                        targetMeetingPos = new Vector3(targetHit.position.x, targetPos.y, initHit.position.z);
                        return;
                    }
                }
            }
        }

        // Fallback: Use exact midpoint without validation (will clip but guarantees visual alignment)
        Vector3 finalMid = midpointsToTest[0];
        float fallBackOffset = offsetsToTest[0];
        initiatorMeetingPos = initiatorIsOnLeft ? new Vector3(finalMid.x - fallBackOffset, initPos.y, finalMid.z) : new Vector3(finalMid.x + fallBackOffset, initPos.y, finalMid.z);
        targetMeetingPos = initiatorIsOnLeft ? new Vector3(finalMid.x + fallBackOffset, targetPos.y, finalMid.z) : new Vector3(finalMid.x - fallBackOffset, targetPos.y, finalMid.z);

        // Sanity clamp X axis to NavMesh for safety, but enforce strict Z parity for 2.5D visual requirement
        if (UnityEngine.AI.NavMesh.SamplePosition(initiatorMeetingPos, out UnityEngine.AI.NavMeshHit finalInitHit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
            initiatorMeetingPos = new Vector3(finalInitHit.position.x, initiatorMeetingPos.y, initiatorMeetingPos.z);
        
        if (UnityEngine.AI.NavMesh.SamplePosition(targetMeetingPos, out UnityEngine.AI.NavMeshHit finalTargetHit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
            targetMeetingPos = new Vector3(finalTargetHit.position.x, targetMeetingPos.y, initiatorMeetingPos.z);
    }

    private System.Collections.IEnumerator MoveToInteractionRoutine(Character target, ICharacterInteractionAction forcedFirstAction, Action onPositioned)
    {
        float timeoutTimer = 0f;
        const float TIMEOUT_DURATION = 5f;
        var movement = _character.CharacterMovement;
        var detector = _character.GetComponent<CharacterInteractionDetector>();
        var targetInteractable = target.CharacterInteractable;
        
        // Security checks against NaN/stutter loops for Navmesh
        float lastRouteRequestTime = 0f;
        Vector3 lastDesiredPos = Vector3.positiveInfinity;

        // --- STATIC POSITIONING ---
        Vector3 staticInitPos = Vector3.zero;
        Vector3 staticTargetPos = Vector3.zero;
        if (!target.IsPlayer())
        {
            CalculateValidMeetingPositions(_character, target, out staticInitPos, out staticTargetPos);
        }

        while (true)
        {
            if (target == null || !target.IsFree())
            {
                Debug.LogWarning($"<color=orange>[Interaction]</color> Cible perdue ou occupée pendant le mouvement.");
                if (movement != null) movement.Stop();
                EndInteraction();
                yield break;
            }

            timeoutTimer += Time.deltaTime;
            if (timeoutTimer > TIMEOUT_DURATION)
            {
                Debug.LogWarning($"<color=orange>[Interaction]</color> Timeout de positionnement pour {_character.CharacterName} vers {target.CharacterName}. ABORT.");
                if (movement != null) movement.Stop();
                EndInteraction();
                yield break;
            }

            bool isCloseEnough = false;
            bool targetInPosition = false;

            // Target Pos Logic
            Vector3 targetPos = target.transform.position;
            Vector3 initiatorMeetingPos = staticInitPos;
            Vector3 targetMeetingPos = staticTargetPos;

            // GESTION DU JOUEUR : S'il est la cible, on ne le force pas à bouger, l'initiateur s'adapte à LUI.
            if (target.IsPlayer())
            {
                float currentDistX = Mathf.Abs(_character.transform.position.x - targetPos.x);
                float finalXDist = Mathf.Clamp(currentDistX, 4f, 6f);
                float xOffset = _character.transform.position.x > targetPos.x ? finalXDist : -finalXDist;
                
                // Force initiator to perfectly align Z with Player
                Vector3 dynamicDesiredInit = new Vector3(targetPos.x + xOffset, _character.transform.position.y, targetPos.z);
                
                // Ensure dynamic desired is somewhat valid
                if (UnityEngine.AI.NavMesh.SamplePosition(dynamicDesiredInit, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    initiatorMeetingPos = new Vector3(hit.position.x, dynamicDesiredInit.y, hit.position.z);
                }
                else
                {
                    initiatorMeetingPos = dynamicDesiredInit;
                }
                targetMeetingPos = targetPos;
                targetInPosition = true; // Le joueur est toujours considéré en position (passif)
            }
            // GESTION NPC : Si le NPC cible n'a pas encore commencé à se positionner, on lui dit d'y aller
            else if (target.CharacterInteraction != null)
            {
                if (!target.CharacterInteraction.IsPositioning && !target.CharacterInteraction.IsPositioned)
                {
                    target.CharacterInteraction.StartTargetPositioning(targetMeetingPos, _character);
                }
                
                // On vérifie s'il est arrivé
                targetInPosition = target.CharacterInteraction.IsPositioned;
            }

            float distDelta = Vector3.Distance(new Vector3(_character.transform.position.x, 0, _character.transform.position.z), 
                                               new Vector3(initiatorMeetingPos.x, 0, initiatorMeetingPos.z));

            // NEW ARRIVAL LOGIC
            if (movement != null && MWI.AI.NavMeshUtility.HasAgentReachedDestination(movement, 0.2f))
            {
                isCloseEnough = true;
            }

            // Fallback checking
            if (distDelta <= 0.2f)
            {
                 isCloseEnough = true;
            }

            if (isCloseEnough && targetInPosition)
            {
                _character.CharacterVisual?.FaceCharacter(target);
                SetPositioned(true);
                IsPositioning = false;
                if (movement != null) movement.Stop();
                
                ExecuteInteraction(target, forcedFirstAction, onPositioned);
                yield break;
            }

            SetPositioned(false);
            IsPositioning = true;
            if (movement != null)
            {
                movement.Resume();
                
                // --- SÉCURITÉ DE THREAD NAVMESH ---
                bool hasPathFailed = (Time.unscaledTime - lastRouteRequestTime > 0.2f) && (movement.Agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.Agent.hasPath && !movement.Agent.pathPending));

                if (Vector3.Distance(lastDesiredPos, initiatorMeetingPos) > 1f || hasPathFailed)
                {
                    movement.SetDestination(initiatorMeetingPos);
                    lastDesiredPos = initiatorMeetingPos;
                    lastRouteRequestTime = Time.unscaledTime;
                }
            }

            yield return null;
        }
    }

    private void HandleTargetStateChanged(Character character, bool isInteracting)
    {
        // Si la cible avec laquelle on voulait interagir commence une interaction avec QUELQU'UN D'AUTRE...
        if (isInteracting && _pendingTarget != null)
        {
            if (_pendingTarget.CharacterInteraction.CurrentTarget != _character)
            {
                Debug.Log($"<color=orange>[Interaction]</color> {_character.CharacterName} annule son approche, car {_pendingTarget.CharacterName} a commencé une interaction avec quelqu'un d'autre.");
                
                // On nettoie la coroutine d'approche AVANT d'appeler EndInteraction pour s'assurer qu'elle s'arrête bien
                if (_activeDialogueCoroutine != null)
                {
                    StopCoroutine(_activeDialogueCoroutine);
                    _activeDialogueCoroutine = null;
                }
                
                EndInteraction();
            }
        }
    }

    // --- NOUVEAU: Logique de positionnement pour la cible ---
    public void StartTargetPositioning(Vector3 targetMeetingPos, Character initiator)
    {
        if (_character.IsPlayer()) return; // Le joueur ne bouge pas automatiquement
        
        IsPositioning = true;
        SetPositioned(false);
        _character.CharacterVisual?.CharacterAnimator?.StopLocomotion();
        
        if (_character.Controller != null) _character.Controller.Freeze();
        
        if (_activeDialogueCoroutine != null) StopCoroutine(_activeDialogueCoroutine);
        _activeDialogueCoroutine = StartCoroutine(TargetPositioningRoutine(targetMeetingPos, initiator));
    }

    private System.Collections.IEnumerator TargetPositioningRoutine(Vector3 destination, Character initiator)
    {
        float timeoutTimer = 0f;
        const float TIMEOUT_DURATION = 5f;
        var movement = _character.CharacterMovement;
        
        float lastRouteRequestTime = 0f;
        Vector3 lastDesiredPos = Vector3.positiveInfinity;

        while (true)
        {
            // Vérification de sécurité: l'initiateur a-t-il annulé sa demande?
            if (initiator == null || (!initiator.CharacterInteraction.IsPositioning && !initiator.CharacterInteraction.IsInteracting))
            {
                Debug.Log($"<color=orange>[Interaction Target]</color> L'initiateur a annulé ou disparu. { _character.CharacterName } abandonne le positionnement.");
                EndInteraction();
                yield break;
            }

            timeoutTimer += Time.deltaTime;
            if (timeoutTimer > TIMEOUT_DURATION)
            {
                Debug.LogWarning($"<color=orange>[Interaction Target]</color> Timeout de positionnement pour { _character.CharacterName }.");
                EndInteraction();
                yield break;
            }

            float distDelta = Vector3.Distance(new Vector3(_character.transform.position.x, 0, _character.transform.position.z), 
                                               new Vector3(destination.x, 0, destination.z));

            bool isCloseEnough = false;

            if (movement != null && MWI.AI.NavMeshUtility.HasAgentReachedDestination(movement, 0.2f))
            {
                isCloseEnough = true;
            }

            if (distDelta <= 0.2f)
            {
                isCloseEnough = true;
            }

            if (isCloseEnough)
            {
                _character.CharacterVisual?.FaceCharacter(initiator);
                SetPositioned(true);
                IsPositioning = false;
                if (movement != null) movement.Stop();
                yield break;
            }

            if (movement != null)
            {
                movement.Resume();
                bool hasPathFailed = (Time.unscaledTime - lastRouteRequestTime > 0.2f) && (movement.Agent.pathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.Agent.hasPath && !movement.Agent.pathPending));

                if (Vector3.Distance(lastDesiredPos, destination) > 1f || hasPathFailed)
                {
                    movement.SetDestination(destination);
                    lastDesiredPos = destination;
                    lastRouteRequestTime = Time.unscaledTime;
                }
            }

            if (movement != null && MWI.AI.NavMeshUtility.HasPathFailed(movement, lastRouteRequestTime, 0.4f))
            {
                Debug.LogWarning($"<color=orange>[Interaction]</color> Path Failed pour { _character.CharacterName } (cible inatteignable).");
                EndInteraction();
                yield break;
            }

            yield return null;
        }
    }

    private void ExecuteInteraction(Character target, ICharacterInteractionAction forcedFirstAction, Action onPositioned)
    {
        if (target == null) return;
        
        // On est arrivé, on se désabonne de l'événement d'approche
        if (_pendingTarget != null)
        {
            _pendingTarget.CharacterInteraction.OnInteractionStateChanged -= HandleTargetStateChanged;
            _pendingTarget = null;
        }
        
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
        if (_character.Controller != null && !_character.IsPlayer()) _character.Controller.Freeze();

        ulong targetId = target.NetworkObject != null ? target.NetworkObject.NetworkObjectId : 0;
        SyncInteractionStateClientRpc(targetId, true);

        if (_activeDialogueCoroutine != null) StopCoroutine(_activeDialogueCoroutine);
        _activeDialogueCoroutine = StartCoroutine(DialogueSequence(_character, target, forcedFirstAction));
        
        onPositioned?.Invoke();
    }

    [Rpc(SendTo.NotServer)]
    private void SyncInteractionStateClientRpc(ulong targetId, bool isInteracting)
    {
        Character target = null;
        if (targetId > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var netObj))
            target = netObj.GetComponent<Character>();

        if (isInteracting)
        {
            CurrentTarget = target;
            IsPositioned = true;
            IsPositioning = false;
            _character.CharacterVisual?.SetLookTarget(target);
            OnInteractionStateChanged?.Invoke(target, true);
        }
        else
        {
            Character previousTarget = _currentTarget;
            _currentTarget = null;
            IsPositioned = false;
            IsPositioning = false;
            _playerPendingAction = null;
            
            if (_character.IsPlayer()) OnPlayerTurnEnded?.Invoke(previousTarget);
            OnInteractionStateChanged?.Invoke(previousTarget, false);
            _character.CharacterVisual?.ClearLookTarget();
        }
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
            ICharacterInteractionAction actionExecuted = null;

            if (forcedFirstAction != null && totalExchanges == 0)
            {
                forcedFirstAction.Execute(currentSpeaker, currentListener);
                actionExecuted = forcedFirstAction;
            }
            else if (currentSpeaker.Controller is NPCController npc)
            {
                var action = npc.GetRandomSocialAction(currentListener);
                action.Execute(currentSpeaker, currentListener);
                actionExecuted = action;
            }
            else if (currentSpeaker.IsPlayer())
            {
                var playerInteraction = currentSpeaker.CharacterInteraction;
                
                // Trigger Owner Client's UI Native Timer and Event
                ulong listenerId = currentListener.NetworkObject != null ? currentListener.NetworkObject.NetworkObjectId : 0;
                playerInteraction.NotifyPlayerTurnStartedClientRpc(listenerId);
                
                playerInteraction._playerPendingAction = null;
                float waitTimer = 0f;
                const float PLAYER_WAIT_DELAY = 8f;

                while (playerInteraction._playerPendingAction == null && IsInteracting && waitTimer < PLAYER_WAIT_DELAY)
                {
                    waitTimer += Time.deltaTime;
                    // Note: Client UI ticks its own timer natively. We just wait on the server.
                    yield return null;
                }

                playerInteraction.NotifyPlayerTurnEndedClientRpc(listenerId);

                if (!IsInteracting || playerInteraction._playerPendingAction == null)
                {
                    Debug.Log("<color=yellow>[Interaction]</color> Le joueur n'a pas répondu à temps ou l'interaction a été rompue. Fin de l'échange.");
                    break;
                }

                actionExecuted = playerInteraction._playerPendingAction;
                playerInteraction._playerPendingAction = null;
            }

            // --- NEW/FIX: Wait if an invitation is pending (thinking/responding phase) ---
            // Moved OUTSIDE the block so forcedFirstAction (like AskForJob) also waits!
            if (currentListener.CharacterInvitation != null && actionExecuted != null)
            {
                // Give it a frame to register that it's pending if started by action.Execute
                yield return null; 
                while (currentListener.CharacterInvitation.HasPendingInvitation)
                {
                    yield return new WaitForSeconds(0.2f);
                }
                
                // If it was an invitation, we wait an extra bit for the response bubble to be readable
                if (actionExecuted is InteractionInvitation)
                {
                    yield return new WaitForSecondsRealtime(2.0f);
                }
            }

            totalExchanges++;
            if (totalExchanges >= MAX_EXCHANGES) break;

            // 2. Attente aléatoire avant la réponse (entre 2.5 et 5 secondes)
            // Mais on s'assure d'abord d'attendre a minima la fin de la bulle de texte
            while (currentSpeaker.CharacterSpeech != null && currentSpeaker.CharacterSpeech.IsTyping)
            {
                yield return null;
            }

            float randomDelay = UnityEngine.Random.Range(1.0f, 2.5f); // Délai réduit car on attend déjà la fin de la bulle
            yield return new WaitForSecondsRealtime(randomDelay);

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
        // On s'assure que toutes les bulles de texte ont fini d'être tapées avant de libérer les personnages
        while ((initiator.CharacterSpeech != null && initiator.CharacterSpeech.IsTyping) ||
               (target.CharacterSpeech != null && target.CharacterSpeech.IsTyping))
        {
            yield return null;
        }

        // --- DÉLAI DE FIN D'INTERACTION ---
        // Petit délai supplémentaire (ex: 2 secondes) pour que les personnages ne s'enfuient pas immédiatement
        yield return new WaitForSecondsRealtime(2.0f);

        Debug.Log($"<color=cyan>[Dialogue]</color> Fin de la séquence après {totalExchanges} échanges.");
        EndInteraction();
    }

    private Coroutine _clientTimerCoroutine;

    [Rpc(SendTo.Owner)]
    private void NotifyPlayerTurnStartedClientRpc(ulong listenerId)
    {
        Character listener = null;
        if (listenerId > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(listenerId, out var netObj))
            listener = netObj.GetComponent<Character>();

        NotifyPlayerTurnStarted(listener);
        
        if (_clientTimerCoroutine != null) StopCoroutine(_clientTimerCoroutine);
        _clientTimerCoroutine = StartCoroutine(ClientTurnTimerRoutine(8f)); // Hardcoded PLAYER_WAIT_DELAY
    }

    private System.Collections.IEnumerator ClientTurnTimerRoutine(float duration)
    {
        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            NotifyPlayerTurnTimerUpdated(Mathf.Clamp01(1f - (timer / duration)));
            yield return null;
        }
    }

    [Rpc(SendTo.Owner)]
    private void NotifyPlayerTurnEndedClientRpc(ulong listenerId)
    {
        if (_clientTimerCoroutine != null) StopCoroutine(_clientTimerCoroutine);
        _clientTimerCoroutine = null;

        Character listener = null;
        if (listenerId > 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(listenerId, out var netObj))
            listener = netObj.GetComponent<Character>();

        NotifyPlayerTurnEnded(listener);
    }

    public void EndInteraction()
    {
        // Nettoyage garanti de l'état d'approche / coroutine, même si on n'a pas encore de CurrentTarget
        if (_pendingTarget != null)
        {
            _pendingTarget.CharacterInteraction.OnInteractionStateChanged -= HandleTargetStateChanged;
            _pendingTarget = null;
        }

        if (_activeDialogueCoroutine != null)
        {
            StopCoroutine(_activeDialogueCoroutine);
            _activeDialogueCoroutine = null;
        }

        // --- NEW: S'assurer que le mouvement est bien stoppé en cas d'annulation avant positionnement ---
        if (!IsPositioned && _character.CharacterMovement != null)
        {
            _character.CharacterMovement.Stop();
        }

        if (_currentTarget == null && !IsPositioning) return;

        Character previousTarget = _currentTarget;
        _currentTarget = null;
        IsPositioned = false;
        IsPositioning = false;
        _playerPendingAction = null;

        // --- Safety: notify turn ended + interaction state for abrupt interruptions ---
        // Works for all characters, not just the player.
        if (_character.IsPlayer())
        {
            OnPlayerTurnEnded?.Invoke(previousTarget);
        }
        OnInteractionStateChanged?.Invoke(previousTarget, false);

        // Libérer le regard
        _character.CharacterVisual?.ClearLookTarget();

        // --- NEW: Clean up redundant movement plans towards the interlocutor ---
        if (previousTarget != null)
        {
            ClearRedundantMovement(previousTarget);
        }

        // --- NEW: Clear behaviours BEFORE unfreezing ---
        // This ensures the last command given to the movement system isn't 'Stop' 
        // after we've already tried to Resume via Unfreeze.
        
        // Target cleanup
        if (previousTarget != null)
        {
            if (previousTarget.Controller is NPCController targetNpc)
            {
                if (previousTarget.GetComponent<NPCBehaviourTree>() != null)
                {
                    targetNpc.ClearBehaviours();
                }
            }
            if (previousTarget.Controller != null) previousTarget.Controller.Unfreeze();
            
            // Si la cible était aussi en train de se positionner vers nous passivement, on la libère
            if (previousTarget.CharacterInteraction.IsPositioning)
            {
                previousTarget.CharacterInteraction.EndInteraction();
            }
        }

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
        if (previousTarget != null)
        {
            if (previousTarget.CharacterInteraction.CurrentTarget == _character)
            {
                previousTarget.CharacterInteraction.EndInteraction();
            }
        }
        
        if (IsServer) SyncInteractionStateClientRpc(0, false);
    }

    internal void SetInteractionTargetInternal(Character target)
    {
        if (!_character.IsFree()) return; // Fail-safe supplémentaire

        // Stop any pending interaction coroutines (e.g. walking to another NPC)
        if (_activeDialogueCoroutine != null)
        {
            StopCoroutine(_activeDialogueCoroutine);
            _activeDialogueCoroutine = null;
        }

        if (_pendingTarget != null)
        {
            _pendingTarget.CharacterInteraction.OnInteractionStateChanged -= HandleTargetStateChanged;
            _pendingTarget = null;
        }

        CurrentTarget = target;
        IsPositioned = true; // La cible est passivement prête
        IsPositioning = false; // Plus besoin
        
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
        if (!IsPositioned) return;

        if (IsOwner && !IsServer)
        {
            PerformInteractionServerRpc(action.GetType().AssemblyQualifiedName);
            return;
        }

        Debug.Log($"<color=green>[Interaction]</color> {_character.CharacterName} exécute {action.GetType().Name} sur {_currentTarget.CharacterName}");
        action.Execute(_character, _currentTarget);

        // Always set _playerPendingAction when in an interaction.
        // The DialogueSequence reads this from the player's CharacterInteraction.
        if (IsInteracting)
        {
            _playerPendingAction = action;
        }
    }

    [Rpc(SendTo.Server)]
    private void PerformInteractionServerRpc(string actionTypeName)
    {
        try
        {
            Type actionType = Type.GetType(actionTypeName);
            if (actionType != null && typeof(ICharacterInteractionAction).IsAssignableFrom(actionType))
            {
                var action = (ICharacterInteractionAction)Activator.CreateInstance(actionType);
                PerformInteraction(action);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Interaction] Failed to deserialize action type {actionTypeName}: {e.Message}");
        }
    }

    private void ClearRedundantMovement(Character target)
    {
        if (_character.Controller == null || target == null) return;
        var npc = _character.Controller as NPCController;
        if (npc == null) return;
        
        // Native movement manages itself through the Coroutine and CharacterMovement natively.
    }

    protected override void HandleIncapacitated(Character character)
    {
        if (IsInteractionProcessActive)
        {
            EndInteraction();
        }
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (inCombat && IsInteractionProcessActive)
        {
            EndInteraction();
        }
    }
}
