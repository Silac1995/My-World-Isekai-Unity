using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterGameController : MonoBehaviour
{
    [SerializeField] protected CharacterVisual _characterVisual;
    [SerializeField] protected Character _character;
    [SerializeField] protected CharacterMovement _characterMovement;
    protected bool _wasDoingAction;
    protected float _actionCooldownTimer;
    private const float ACTION_RESUME_DELAY = 0.10f;

    private Stack<IAIBehaviour> _behavioursStack = new Stack<IAIBehaviour>();
    public IAIBehaviour CurrentBehaviour => _behavioursStack.Count > 0 ? _behavioursStack.Peek() : null;

    // --- Freeze : stoppe tout (mouvement, IA, animations) ---
    private bool _isFrozen = false;
    public bool IsFrozen => _isFrozen;

    /// <summary>
    /// Stoppe entièrement le NPC : mouvement, path, animations de locomotion.
    /// Le behaviour stack est conservé mais ne tick plus.
    /// </summary>
    public virtual void Freeze()
    {
        _isFrozen = true;
        _characterMovement?.ResetPath();
        _characterMovement?.Stop();
        _character.CharacterVisual?.CharacterAnimator?.StopLocomotion();
    }

    /// <summary>
    /// Reprend le fonctionnement normal du NPC.
    /// </summary>
    public virtual void Unfreeze()
    {
        _isFrozen = false;
        _characterMovement?.Resume();

        // Forcer le BT à ticker immédiatement pour éviter un délai visible
        var bt = _character.GetComponent<NPCBehaviourTree>();
        if (bt != null) bt.ForceNextTick();
    }

    // --- PROPRIÉTÉS DE COMPATIBILITÉ (Pour corriger tes erreurs) ---
    public Character Character => _character;
    public NavMeshAgent Agent => _characterMovement != null ? _characterMovement.Agent : null;
    public CharacterMovement CharacterMovement => _characterMovement;
    public Animator Animator => (_characterVisual != null && _characterVisual.CharacterAnimator != null)
                                 ? _characterVisual.CharacterAnimator.Animator
                                 : null;
    public virtual void Initialize()
    {
        if (_character != null && !_character.IsAlive()) enabled = false;
        
        if (_character != null && _character.CharacterActions != null)
        {
            _character.CharacterActions.OnActionStarted -= HandleActionStarted;
            _character.CharacterActions.OnActionStarted += HandleActionStarted;
            _character.CharacterActions.OnActionFinished -= HandleActionFinished;
            _character.CharacterActions.OnActionFinished += HandleActionFinished;
        }
    }

    private void HandleActionStarted(CharacterAction action)
    {
        _wasDoingAction = true;
        _actionCooldownTimer = ACTION_RESUME_DELAY;
        _characterMovement?.Stop();

        if (action.ShouldPlayGenericActionAnimation)
        {
            Animator?.SetBool(CharacterAnimator.IsDoingAction, true);
        }
    }

    private void HandleActionFinished()
    {
        // Don't reset _wasDoingAction here, let the cooldown timer handle it in Update
        Animator?.SetBool(CharacterAnimator.IsDoingAction, false);
        _character.CharacterVisual?.CharacterAnimator?.ResetActionTriggers();
    }

    protected virtual void OnDestroy()
    {
        if (_character != null && _character.CharacterActions != null)
        {
            _character.CharacterActions.OnActionStarted -= HandleActionStarted;
            _character.CharacterActions.OnActionFinished -= HandleActionFinished;
        }
    }

    protected virtual void Update()
    {
        if (_isFrozen) 
        {
            UpdateVisuals();
            return;
        }

        // Action logic is now event-driven for starting/finishing, 
        // but we still need to handle the cooldown timer here.
        if (_character.CharacterActions.CurrentAction != null)
        {
            _characterMovement?.Stop();
            UpdateVisuals(true); // Force speed 0 and skip flip
            return;
        }

        if (_wasDoingAction)
        {
            _actionCooldownTimer -= Time.deltaTime;

            if (_actionCooldownTimer <= 0)
            {
                _wasDoingAction = false;
                _characterMovement?.Resume();
            }
            else
            {
                _characterMovement?.Stop();
                UpdateVisuals(true); 
                return;
            }
        }

        if (CurrentBehaviour != null)
        {
            if (CurrentBehaviour.IsFinished)
            {
                PopBehaviour();
                return;
            }

            CurrentBehaviour.Act(_character);
        }

        UpdateVisuals();
    }

    private void UpdateVisuals(bool forceIdle = false)
    {
        UpdateAnimations(forceIdle);
        // Only flip if not doing an action and not forced idle
        if (!forceIdle && _character.CharacterActions.CurrentAction == null)
        {
            UpdateFlip();
        }
    }

    // --- M?THODES REQUISES PAR TES BEHAVIOURS ET INTERACTIONS ---

    public void SetBehaviour(IAIBehaviour behaviour) => ResetStackTo(behaviour);

    public void PushBehaviour(IAIBehaviour newBehaviour)
    {
        SafeResume();
        _behavioursStack.Push(newBehaviour);
        newBehaviour.Enter(_character);
    }

    public void PopBehaviour()
    {
        if (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(_character);
        }

        SafeResume();

        if (_behavioursStack.Count == 0 && _character.TryGetComponent<NPCController>(out var npc))
        {
            if (!npc.HasBehaviourTree)
            {
                ResetStackTo(new WanderBehaviour(npc));
            }
        }
    }

    public void ClearBehaviours()
    {
        // ... (existing code)
        while (_behavioursStack.Count > 0)
        {
            IAIBehaviour old = _behavioursStack.Pop();
            old.Exit(_character);
        }

        if (_characterMovement != null)
        {
            _characterMovement.Stop();
            if (Agent != null && Agent.isOnNavMesh)
            {
                Agent.ResetPath();
            }
        }
    }

    public void ResetStackTo(IAIBehaviour baseBehaviour)
    {
        ClearBehaviours();
        SafeResume();
        _behavioursStack.Push(baseBehaviour);
        baseBehaviour.Enter(_character);
    }

    private void SafeResume()
    {
        if (_isFrozen) return; // Ne JAMAIS reprendre le mouvement si le cerveau est gelé !

        // Une s?curit? de fer : on ne Resume() QUE si on n'est pas en train de faire qqc 
        // ET qu'on a fini le petit temps de settling.
        if (_character.CharacterActions.CurrentAction != null || _wasDoingAction) 
        {
            _characterMovement.Stop(); // On double-lock en s?curit?
            return;
        }
        
        _characterMovement.Resume();
    }

    public bool HasBehaviour<T>() where T : IAIBehaviour => _behavioursStack.Any(b => b is T);

    public T GetCurrentBehaviour<T>() where T : class, IAIBehaviour => CurrentBehaviour as T;

    public List<string> GetBehaviourStackNames() => _behavioursStack.Select(b => b.GetType().Name).ToList();

    // --- LOGIQUE VISUELLE ---

    protected virtual void UpdateAnimations(bool forceIdle = false)
    {
        if (Animator == null || _characterMovement == null) return;

        Vector3 velocity = _characterMovement.GetVelocity();
        float speed = new Vector3(velocity.x, 0, velocity.z).magnitude;

        if (forceIdle || speed < 0.1f) 
        {
            speed = 0f;
        }

        // Envoi ? l'Animator
        Animator.SetFloat(CharacterAnimator.VelocityX, speed);
        Animator.SetBool(CharacterAnimator.IsWalking, speed > 0f);

        // Sol
        Animator.SetBool(CharacterAnimator.IsGrounded, _characterMovement.IsGrounded());

        // --- Walk Forward/Backward ---
        // On délègue le calcul des paramètres de direction à CharacterVisual
        _characterVisual?.UpdateWalkingParameters(velocity);
    }

    protected virtual void UpdateFlip()
    {
        if (_characterVisual == null || _characterMovement == null) return;

        // --- SÉCURITÉ INTERACTION : On ne flip pas par vélocité quand on est "en place" ---
        // Une fois positionné, c'est l'IA (InteractBehaviour) qui gère l'orientation 
        // pour faire face à l'interlocuteur. On bloque la vélocité ici pour éviter les glitchs.
        if (_character.CharacterInteraction != null && _character.CharacterInteraction.IsPositioned) return;

        Vector3 velocity = _characterMovement.GetVelocity();
        if (velocity.sqrMagnitude > 0.01f) _characterVisual.UpdateFlip(velocity);
    }

    protected bool IsGrounded() => _characterMovement != null && _characterMovement.IsGrounded();
}
