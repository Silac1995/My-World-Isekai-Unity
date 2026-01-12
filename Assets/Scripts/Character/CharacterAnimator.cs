using UnityEngine;

public class CharacterAnimator : MonoBehaviour
{
    [SerializeField] private CharacterVisual _characterVisual;
    [SerializeField] private Animator _animator;

    // Référence vers l'Override Controller actuel pour pouvoir le modifier
    private AnimatorOverrideController _overrideController;

    public Animator Animator => _animator;

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