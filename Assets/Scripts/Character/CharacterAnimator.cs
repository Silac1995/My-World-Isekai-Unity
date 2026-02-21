using System.Collections.Generic;
using UnityEngine;

public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private CharacterVisual _characterVisual;
    [SerializeField] private Animator _animator;
    [SerializeField] private RuntimeAnimatorController _civilAnimatorController;
    [SerializeField] private CharacterCombat _characterCombat;

        public RuntimeAnimatorController CivilAnimatorController => _civilAnimatorController;

    #region Animation Methods
    private float _lastAttackTime;

    public void PlayMeleeAttack()
    {
        // --- S?CURIT? TEMPORELLE ---
        // On interdit deux d?clenchements en moins de 0.2s pour ?viter les ghost-triggers
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
        
        // On nettoie systématiquement les triggers d'actions connus pour éviter l'empilement
        _animator.ResetTrigger(MeleeAttackTrigger);
        _animator.ResetTrigger(ActionTrigger);
        
        // On active le nouveau
        _animator.SetTrigger(triggerHash);
    }

    public void ResetActionTriggers()
    {
        if (_animator == null) return;
        _animator.ResetTrigger(MeleeAttackTrigger);
        _animator.ResetTrigger(ActionTrigger);
    }
    #endregion

    #region Animation Events Bridge
    public void AE_SpawnCombatStyleAttackInstance()
    {
        // --- BREAKTHROUGH FIX : On consomme le trigger ICI ---
        // C'est le moment o? l'attaque "commence" r?ellement son effet. 
        // En resettant ici, on garantit que l'Animator ne pourra JAMAIS relancer l'animation 
        // une deuxi?me fois juste apr?s, car le trigger sera d?j? mort.
        ResetActionTriggers();

        if (_characterCombat != null)
            _characterCombat.SpawnCombatStyleAttackInstance();
    }

    public void AE_DespawnCombatStyleAttackInstance()
    {
        // S?curit? suppl?mentaire en fin d'animation
        ResetActionTriggers();

        if (_characterCombat != null)
            _characterCombat.DespawnCombatStyleAttackInstance();
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
    private Dictionary<string, float> _clipDurations = new Dictionary<string, float>();
    public static readonly int IsWalking = Animator.StringToHash("isWalking");

    public Animator Animator => _animator;

    private void Awake()
    {
        if (_animator == null) _animator = GetComponent<Animator>();
        CacheClipDurations();
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
        if (_animator != null)
            _animator.SetFloat(VelocityX, speed);
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
        return 0.8f; // Fallback par défaut
    }
}
