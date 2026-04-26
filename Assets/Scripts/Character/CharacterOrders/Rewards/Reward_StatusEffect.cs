using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Applies a positive CharacterStatusEffect to the receiver on compliance.
    /// Works with null issuer (anonymous/system orders).
    ///
    /// The asset field uses CharacterStatusEffect — the project's canonical SO type for status effects.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Rewards/Status Effect", fileName = "Reward_StatusEffect_New")]
    public class Reward_StatusEffect : ScriptableObject, IOrderReward
    {
        [Tooltip("Status effect to apply to the receiver on compliance.")]
        [SerializeField] private CharacterStatusEffect _statusEffect;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (receiver == null || _statusEffect == null) return;

            if (receiver.StatusManager == null)
            {
                Debug.LogWarning($"<color=yellow>[Reward_StatusEffect]</color> Receiver {receiver.CharacterName} has no StatusManager; skipping.");
                return;
            }

            // ApplyEffect(CharacterStatusEffect, Character caster) is the canonical entry point.
            receiver.StatusManager.ApplyEffect(_statusEffect, issuer?.AsCharacter);
            Debug.Log($"<color=green>[Order]</color> {receiver.CharacterName} gains reward status '{_statusEffect.StatusEffectName}'.");
        }
    }
}
