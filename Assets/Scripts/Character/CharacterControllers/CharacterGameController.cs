using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public abstract class CharacterGameController : CharacterSystem
{
    [SerializeField] protected CharacterVisual _characterVisual;
    [SerializeField] protected CharacterMovement _characterMovement;
    protected bool _wasDoingAction;
    protected float _actionCooldownTimer;
    private const float ACTION_RESUME_DELAY = 0.10f;

    // --- Freeze: stops everything (movement, AI, animations) ---
    private bool _isFrozen = false;
    public bool IsFrozen => _isFrozen;

    /// <summary>
    /// Fully stops the NPC: movement, path, locomotion animations.
    /// The behaviour stack is preserved but stops ticking.
    /// </summary>
    public virtual void Freeze()
    {
        _isFrozen = true;
        _characterMovement?.ResetPath();
        _characterMovement?.Stop();
        _character.CharacterVisual?.CharacterAnimator?.StopLocomotion();
    }

    /// <summary>
    /// Resumes normal NPC operation.
    /// </summary>
    public virtual void Unfreeze()
    {
        _isFrozen = false;
        _characterMovement?.Resume();

        // Force the BT to tick immediately to avoid a visible delay
        if (_character.TryGet<NPCBehaviourTree>(out var bt)) bt.ForceNextTick();
    }

    // --- COMPATIBILITY PROPERTIES ---
    public NavMeshAgent Agent => _characterMovement != null ? _characterMovement.Agent : null;
    public CharacterMovement CharacterMovement => _characterMovement;
    public Character Character => _character;
    public Animator Animator => (_characterVisual != null && _characterVisual.CharacterAnimator != null)
                                 ? _characterVisual.CharacterAnimator.Animator
                                 : null;

    protected override void HandleIncapacitated(Character character)
    {
        enabled = false;
        
        if (this is NPCController npc)
        {
            npc.ClearBehaviours();
        }
    }

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
        // Walking actions (AllowsMovementDuringAction = true) keep the agent path-following;
        // stationary actions (the default) stop it so the actor doesn't drift while doing things.
        // Only stationary actions arm the post-action settling cooldown — walking actions never
        // paused movement, so they have no jerk to settle out of.
        if (!action.AllowsMovementDuringAction)
        {
            _wasDoingAction = true;
            _actionCooldownTimer = ACTION_RESUME_DELAY;
            _characterMovement?.Stop();
        }

        if (action.ShouldPlayGenericActionAnimation)
        {
            if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
            {
                _characterVisual.CharacterAnimator.SetAnimBoolSafely(CharacterAnimator.IsDoingAction, true);
            }
            else
            {
                Animator?.SetBool(CharacterAnimator.IsDoingAction, true);
            }
        }
    }

    private void HandleActionFinished()
    {
        // Don't reset _wasDoingAction here, let the cooldown timer handle it in Update
        if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
        {
            _characterVisual.CharacterAnimator.SetAnimBoolSafely(CharacterAnimator.IsDoingAction, false);
            _characterVisual.CharacterAnimator.ResetActionTriggers();
        }
        else
        {
            Animator?.SetBool(CharacterAnimator.IsDoingAction, false);
            _character.CharacterVisual?.CharacterAnimator?.ResetActionTriggers();
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_character != null && _character.CharacterActions != null)
        {
            _character.CharacterActions.OnActionStarted -= HandleActionStarted;
            _character.CharacterActions.OnActionFinished -= HandleActionFinished;
        }
    }

    protected virtual void Update()
    {
        // 1. Check if we are performing a specific CharacterAction (animation/logic sequence).
        //    Walking actions (AllowsMovementDuringAction = true) skip this gate so they can drive
        //    the NavMeshAgent themselves; stationary actions (the default) hard-stop movement
        //    every frame to keep the actor anchored while the action animation plays.
        var currentAction = _character.CharacterActions.CurrentAction;
        if (currentAction != null && !currentAction.AllowsMovementDuringAction)
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
        // Only flip if not forced idle and (no action active OR the active action allows movement).
        // Walking actions (AllowsMovementDuringAction = true) need flip to follow walk direction.
        var currentAction = _character.CharacterActions.CurrentAction;
        bool actionAllowsFlip = currentAction == null || currentAction.AllowsMovementDuringAction;
        if (!forceIdle && actionAllowsFlip)
        {
            UpdateFlip();
        }
    }

    // --- UTILITY METHODS ---

    protected void SafeResume()
    {
        if (_isFrozen) return; // NEVER resume movement if the brain is frozen!

        // Hard safety: only call Resume() if we are not doing a stationary action
        // AND the settling cooldown has finished. Walking actions (AllowsMovementDuringAction
        // = true) are explicitly allowed to keep moving — Resume is fine there too.
        var currentAction = _character.CharacterActions.CurrentAction;
        bool stationaryActionActive = currentAction != null && !currentAction.AllowsMovementDuringAction;
        if (stationaryActionActive || _wasDoingAction)
        {
            _characterMovement.Stop(); // Double-lock as a safety net
            return;
        }

        _characterMovement.Resume();
    }

    // --- VISUAL LOGIC ---

    protected virtual void UpdateAnimations(bool forceIdle = false)
    {
        if (Animator == null || _characterMovement == null) return;

        Vector3 velocity = _characterMovement.GetVelocity();
        float speed = new Vector3(velocity.x, 0, velocity.z).magnitude;

        if (forceIdle || speed < 0.1f) 
        {
            speed = 0f;
        }

        // Send to Animator
        if (_characterVisual != null && _characterVisual.CharacterAnimator != null)
        {
            _characterVisual.CharacterAnimator.SetAnimFloatSafely(CharacterAnimator.VelocityX, speed);
            _characterVisual.CharacterAnimator.SetAnimBoolSafely(CharacterAnimator.IsWalking, speed > 0f);
            
            // Grounded
            _characterVisual.CharacterAnimator.SetAnimBoolSafely(CharacterAnimator.IsGrounded, _characterMovement.IsGrounded());
        }
        else
        {
            Animator.SetFloat(CharacterAnimator.VelocityX, speed);
            Animator.SetBool(CharacterAnimator.IsWalking, speed > 0f);

            // Grounded
            Animator.SetBool(CharacterAnimator.IsGrounded, _characterMovement.IsGrounded());
        }

        // --- Walk Forward/Backward ---
        // Delegate direction parameter calculation to CharacterVisual
        _characterVisual?.UpdateWalkingParameters(velocity);
    }

    protected virtual void UpdateFlip()
    {
        if (_characterVisual == null || _characterMovement == null) return;

        // --- INTERACTION SAFETY: Do not flip by velocity when the character is "in position" ---
        // Once positioned, the AI (InteractBehaviour) handles orientation
        // to face the interlocutor. We block velocity-based flipping here to avoid glitches.
        if (_character.CharacterInteraction != null && _character.CharacterInteraction.IsPositioned) return;

        Vector3 velocity = _characterMovement.GetVelocity();
        if (velocity.sqrMagnitude > 0.01f) _characterVisual.UpdateFlip(velocity);
    }

    protected bool IsGrounded() => _characterMovement != null && _characterMovement.IsGrounded();
}
