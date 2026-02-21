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
            ResetStackTo(new WanderBehaviour(npc));
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

        // Sol
        Animator.SetBool(CharacterAnimator.IsGrounded, _characterMovement.IsGrounded());
    }

    protected virtual void UpdateFlip()
    {
        if (_characterVisual == null) return;
        Vector3 velocity = _characterMovement.GetVelocity();
        if (velocity.sqrMagnitude > 0.01f) _characterVisual.UpdateFlip(velocity);
    }

    protected bool IsGrounded() => _characterMovement != null && _characterMovement.IsGrounded();
}
