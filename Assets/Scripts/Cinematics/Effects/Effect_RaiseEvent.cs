using UnityEngine;
using UnityEngine.Events;

namespace MWI.Cinematics
{
    /// <summary>
    /// UnityEvent escape hatch. Designers wire callbacks in the Inspector; the effect
    /// invokes them on Apply. Useful for one-off scripted hooks until a more specific
    /// CinematicEffectSO subclass is added (Effect_PlayVFX, Effect_GiveQuest, …).
    /// </summary>
    [CreateAssetMenu(
        fileName = "Effect_RaiseEvent",
        menuName = "MWI/Cinematics/Effects/Raise Event")]
    public class Effect_RaiseEvent : CinematicEffectSO
    {
        [Tooltip("UnityEvent escape hatch — wire any callback you like.")]
        [SerializeField] private UnityEvent _onApply;

        public override void Apply(CinematicContext ctx)
        {
            Debug.Log($"<color=cyan>[Cinematic]</color> Effect_RaiseEvent: firing UnityEvent on scene '{ctx?.Scene?.SceneId}'.");
            _onApply?.Invoke();
        }
    }
}
