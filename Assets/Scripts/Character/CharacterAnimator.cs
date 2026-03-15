using System.Collections.Generic;
using UnityEngine;

public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private CharacterVisual _characterVisual;
    [SerializeField] private Animator _animator;
    [SerializeField] private RuntimeAnimatorController _civilAnimatorController;
    [SerializeField] private CharacterCombat _characterCombat;
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;

    public RuntimeAnimatorController CivilAnimatorController => _civilAnimatorController;

    #region Animation Methods
    private float _lastAttackTime;

    public void PlayMeleeAttack()
    {
        if (Time.time - _lastAttackTime < 0.2f) return;
        _lastAttackTime = Time.time;
        SetTriggerSafely(MeleeAttackTrigger);
    }

    public void PlayPickUpItem()
    {
        SetTriggerSafely(ActionTrigger);
    }

    private void SetTriggerSafely(int triggerHash)
    {
        if (_animator == null) return;
        if (HasParameter(MeleeAttackTrigger)) _animator.ResetTrigger(MeleeAttackTrigger);
        if (HasParameter(ActionTrigger)) _animator.ResetTrigger(ActionTrigger);
        SetAnimTriggerSafely(triggerHash);
    }

    public void ResetActionTriggers()
    {
        if (_animator == null) return;
        if (HasParameter(MeleeAttackTrigger)) _animator.ResetTrigger(MeleeAttackTrigger);
        if (HasParameter(ActionTrigger)) _animator.ResetTrigger(ActionTrigger);
    }

    public void SetDead(bool dead)
    {
        SetAnimBoolSafely(IsDead, dead);
        if (dead) StopLocomotion();
    }

    public void StopLocomotion()
    {
        if (_animator == null) return;
        SetAnimFloatSafely(VelocityX, 0f);
        SetAnimBoolSafely(IsWalking, false);
        SetAnimBoolSafely(IsWalkingBackward, false);
        SetAnimBoolSafely(IsWalkingForward, false);
    }

    public void SetWalkingBackward(bool backward)
    {
        SetAnimBoolSafely(IsWalkingBackward, backward);
    }

    public void SetWalkingForward(bool forward)
    {
        SetAnimBoolSafely(IsWalkingForward, forward);
    }

    public void SetCombat(bool combat)
    {
        SetAnimBoolSafely(IsCombat, combat);
    }

    /// <summary>
    /// Force la synchronisation des paramètres essentiels (mort, combat, etc.)
    /// Utile après un changement de Controller.
    /// </summary>
    public void SyncParameters(Character character, bool isCombat)
    {
        if (_animator == null || character == null) return;

        // On ré-applique l'état de vie/mort
        SetAnimBoolSafely(IsDead, !character.IsAlive());
        
        // On ré-applique l'état de combat
        SetAnimBoolSafely(IsCombat, isCombat);

        // On remet la vélocité à zéro si mort pour éviter de glisser dans la mauvaise anim
        if (!character.IsAlive())
        {
            StopLocomotion();
        }
        else
        {
            // Optionnel : on pourrait aussi sync la velocityX ici si besoin
        }
    }
    #endregion

    #region Animation Events Bridge
    public void AE_SpawnCombatStyleAttackInstance()
    {
        ResetActionTriggers();
        if (_characterCombat != null)
            _characterCombat.SpawnCombatStyleAttackInstance();
    }

    public void AE_DespawnCombatStyleAttackInstance()
    {
        ResetActionTriggers();
        if (_characterCombat != null)
            _characterCombat.DespawnCombatStyleAttackInstance();
    }

    // --- Hands Animation Events ---
    public void AE_SetAllHandsFist()
    {
        if (_bodyPartsController != null && _bodyPartsController.HandsController != null)
            _bodyPartsController.HandsController.SetAllHandsFist();
    }

    public void AE_SetAllHandsNormal()
    {
        if (_bodyPartsController != null && _bodyPartsController.HandsController != null)
            _bodyPartsController.HandsController.SetAllHandsNormal();
    }

    public void AE_SetRightHandFist()
    {
        if (_bodyPartsController != null && _bodyPartsController.HandsController != null)
            _bodyPartsController.HandsController.SetRightHandFist();
    }

    public void AE_SetRightHandNormal()
    {
        if (_bodyPartsController != null && _bodyPartsController.HandsController != null)
            _bodyPartsController.HandsController.SetRightHandNormal();
    }

    public void AE_SetLeftHandFist()
    {
        if (_bodyPartsController != null && _bodyPartsController.HandsController != null)
            _bodyPartsController.HandsController.SetLeftHandFist();
    }

    public void AE_SetLeftHandNormal()
    {
        if (_bodyPartsController != null && _bodyPartsController.HandsController != null)
            _bodyPartsController.HandsController.SetLeftHandNormal();
    }
    #endregion

    public void SetCivilAnimatorController(RuntimeAnimatorController controller)
    {
        _civilAnimatorController = controller;
    }

    // --- CENTRALISATION DES HASHES ---
    public static readonly int IsGrounded = Animator.StringToHash("isGrounded");
    public static readonly int VelocityX = Animator.StringToHash("velocityX");
    public static readonly int ActionTrigger = Animator.StringToHash("Trigger_pickUpItem");
    public static readonly int MeleeAttackTrigger = Animator.StringToHash("Trigger_meleeAttack");
    public static readonly int IsDoingAction = Animator.StringToHash("isDoingAction");
    public static readonly int IsWalking = Animator.StringToHash("isWalking");
    public static readonly int IsWalkingBackward = Animator.StringToHash("isWalkingBackward");
    public static readonly int IsWalkingForward = Animator.StringToHash("isWalkingForward");
    public static readonly int IsDead = Animator.StringToHash("isDead");
    public static readonly int IsCombat = Animator.StringToHash("isCombat");

    private Dictionary<string, float> _clipDurations = new Dictionary<string, float>();

    public Animator Animator => _animator;

    private HashSet<int> _validParameters = new HashSet<int>();

    private void Awake()
    {
        if (_animator == null) _animator = GetComponent<Animator>();
        CacheClipDurations();
        CacheParameters();
    }

    public void CacheParameters()
    {
        if (_animator == null) return;
        _validParameters.Clear();
        foreach (var param in _animator.parameters)
        {
            _validParameters.Add(param.nameHash);
        }
    }

    public bool HasParameter(int hash) => _validParameters.Contains(hash);

    public void SetAnimFloatSafely(int hash, float value)
    {
        if (_animator != null && HasParameter(hash))
            _animator.SetFloat(hash, value);
    }

    public void SetAnimBoolSafely(int hash, bool value)
    {
        if (_animator != null && HasParameter(hash))
            _animator.SetBool(hash, value);
    }

    public void SetAnimTriggerSafely(int hash)
    {
        if (_animator != null && HasParameter(hash))
            _animator.SetTrigger(hash);
    }

    public void CacheClipDurations()
    {
        if (_animator == null) return;
        _clipDurations.Clear();

        if (_animator.runtimeAnimatorController != null)
        {
            foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
            {
                _clipDurations[clip.name] = clip.length;
            }
        }
    }

    public void SetVelocity(float speed)
    {
        SetAnimFloatSafely(VelocityX, speed);
    }

    public float GetCurrentClipDuration()
    {
        if (Animator == null) return 0f;
        AnimatorStateInfo stateInfo = Animator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.length;
    }

    public float GetCachedDuration(string clipName)
    {
        if (_clipDurations.TryGetValue(clipName, out float duration))
            return duration;
        return 0f;
    }

    public float GetMeleeAttackDuration()
    {
        foreach (var pair in _clipDurations)
        {
            if (pair.Key.Contains("MeleeAttack"))
            {
                return pair.Value;
            }
        }
        return 0.8f;
    }
}
