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
        
        // --- NOUVEAU : Tracking immédiat du début d'action ---
        // On n'attend pas l'Update pour savoir qu'on fait quelque chose
        if (_character != null)
        {
            _character.CharacterActions.OnActionStarted -= HandleActionStarted;
            _character.CharacterActions.OnActionStarted += HandleActionStarted;
        }
    }

    private void HandleActionStarted(CharacterAction action)
    {
        _wasDoingAction = true;
        _actionCooldownTimer = ACTION_RESUME_DELAY;
        _characterMovement.Stop();
    }

    protected virtual void OnDestroy()
    {
        if (_character != null && _character.CharacterActions != null)
        {
            _character.CharacterActions.OnActionStarted -= HandleActionStarted;
        }
    }

    protected virtual void Update()
    {
        // Freeze : on ne fait rien du tout, MAIS on continue de mettre à jour 
        // les animations si jamais le personnage est poussé physiquement.
        if (_isFrozen) 
        {
            UpdateAnimations();
            UpdateFlip();
            return;
        }

        if (_character.CharacterActions.CurrentAction != null)
        {
            _wasDoingAction = true;
            _actionCooldownTimer = ACTION_RESUME_DELAY;
            _characterMovement.Stop();

            UpdateAnimations();
            // On ne flip pas pendant une action pour éviter de glitcher l'animation d'attaque (ou autre)
            return;
        }

        // --- NOUVEAU : Cleanup et décompte après l'action ---
        if (_wasDoingAction)
        {
            // On nettoie les drapeaux et triggers dès la fin de l'action pour éviter les pile-ups
            Animator.SetBool(CharacterAnimator.IsDoingAction, false);
            _character.CharacterVisual?.CharacterAnimator?.ResetActionTriggers();

            _actionCooldownTimer -= Time.deltaTime;

            if (_actionCooldownTimer <= 0)
            {
                _wasDoingAction = false;
                _characterMovement.Resume();
            }
            else
            {
                // Pendant le délai "de grâce", on reste immobile
                _characterMovement.Stop();
                UpdateAnimations();
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

            // L'IA gère son propre Resume/Stop
            CurrentBehaviour.Act(_character);
        }

        // Si un behaviour vient d'appeler Freeze() dans son Act(), on assure la transition d'anim
        if (_isFrozen) 
        {
            UpdateAnimations();
            UpdateFlip();
            return;
        }

        UpdateAnimations();
        UpdateFlip();
    }

    // --- M?THODES REQUISES PAR TES BEHAVIOURS ET INTERACTIONS ---

    public void SetBehaviour(IAIBehaviour behaviour) => ResetStackTo(behaviour);

    public void PushBehaviour(IAIBehaviour newBehaviour)
    {
        SafeResume();
        _behavioursStack.Push(newBehaviour);
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

    protected virtual void UpdateAnimations()
    {
        if (Animator == null || _characterMovement == null) return;

        // R?cup?ration de la vitesse physique r?elle
        Vector3 velocity = _characterMovement.GetVelocity();
        float speed = new Vector3(velocity.x, 0, velocity.z).magnitude;

        // --- S?CURIT? ANIMATION : On force le calme pendant les actions ---
        // On ne laisse jamais la vitesse physique (qui peut mettre une frame ? s'arr?ter) 
        // interrompre une animation d'attaque ou le d?lai de repos.
        if (_wasDoingAction || _character.CharacterActions.CurrentAction != null)
        {
            speed = 0f;
        }
        else if (speed < 0.1f) 
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
