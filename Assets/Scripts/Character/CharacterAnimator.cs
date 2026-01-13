using System.Collections.Generic;
using UnityEngine;

public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private CharacterVisual _characterVisual;
    [SerializeField] private Animator _animator;

    // --- CENTRALISATION DES HASHES ---
    // On les met en 'public static readonly' pour que tout le monde puisse les lire
    // sans avoir besoin de faire un Animator.StringToHash() ailleurs.
    public static readonly int IsGrounded = Animator.StringToHash("isGrounded");
    public static readonly int VelocityX = Animator.StringToHash("velocityX");
    public static readonly int ActionTrigger = Animator.StringToHash("Trigger_pickUpItem");
    public static readonly int IsDoingAction = Animator.StringToHash("isDoingAction");
    private Dictionary<string, float> _clipDurations = new Dictionary<string, float>();

    // Référence vers l'Override Controller actuel pour pouvoir le modifier
    private AnimatorOverrideController _overrideController;

    public Animator Animator => _animator;

    private void Awake()
    {
        // Sécurité : on récupère l'animator s'il manque
        if (_animator == null) _animator = GetComponent<Animator>();

        // INDISPENSABLE : On remplit le dictionnaire au lancement
        CacheClipDurations();
    }

    public void CacheClipDurations()
    {
        if (_animator == null) return; // Utilise la variable privée _animator ici
        _clipDurations.Clear();

        foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
        {
            // Debug.Log($"Clip mis en cache : {clip.name} | {clip.length}s");
            _clipDurations[clip.name] = clip.length;
        }
    }

    // Méthode simple pour mettre à jour la vitesse
    public void SetVelocity(float speed)
    {
        if (_animator != null)
            _animator.SetFloat(VelocityX, speed);
    }

    public float GetCurrentClipDuration()
    {
        if (Animator == null) return 0f;

        // On récupère les infos de l'état actuel sur la couche 0 (Base Layer)
        AnimatorStateInfo stateInfo = Animator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.length;
    }

    public float GetCachedDuration(string clipName)
    {
        if (_clipDurations.TryGetValue(clipName, out float duration))
            return duration;

        return 0f;
    }

    //public void Initialize()
    //{
    //    if (_animator == null) _animator = GetComponent<Animator>();

    //    // On crée une instance unique d'Override Controller basée sur l'Animator actuel
    //    // Cela permet de modifier les animations d'un perso sans affecter les autres
    //    _overrideController = new AnimatorOverrideController(_animator.runtimeAnimatorController);
    //    _animator.runtimeAnimatorController = _overrideController;
    //}

    ///// <summary>
    ///// Change le style visuel du combat (ex: Posture débutant vs Maître)
    ///// </summary>
    ///// <param name="styleData">Un objet contenant les clips d'animations du style</param>
    //public void ApplyCombatStyle(CombatStyleSO styleData)
    //{
    //    if (styleData == null || _overrideController == null) return;

    //    // On remplace les clips par défaut par ceux du style de maîtrise
    //    _overrideController["Sword_Idle"] = styleData.IdleClip;
    //    _overrideController["Sword_Attack_1"] = styleData.AttackClip;

    //    Debug.Log($"<color=yellow>[Animator]</color> Style de combat '{styleData.name}' appliqué.");
    //}
}