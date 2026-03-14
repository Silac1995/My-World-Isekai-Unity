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
        // 1. Check if we are performing a specific CharacterAction (animation/logic sequence)
        if (_character.CharacterActions.CurrentAction != null)
        {
            _characterMovement?.Stop();
            UpdateVisuals(true); // Force speed 0 and skip flip
            return;
        }

        // 2. Handle the "settling" cooldown after an action finishes 
        // to prevent immediate jerky movement before the state is stable.
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

        // 4. If frozen (dialogue), early return but still update visuals (idle)
        if (_isFrozen) 
        {
            UpdateVisuals();
            return;
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

    // --- MÉTHODES UTILES ---

    protected void SafeResume()
    {
        if (_isFrozen) return; // Ne JAMAIS reprendre le mouvement si le cerveau est gelé !

        // Une sécurité de fer : on ne Resume() QUE si on n'est pas en train de faire qqc 
        // ET qu'on a fini le petit temps de settling.
        if (_character.CharacterActions.CurrentAction != null || _wasDoingAction) 
        {
            _characterMovement.Stop(); // On double-lock en sécurité
            return;
        }
        
        _characterMovement.Resume();
    }

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
