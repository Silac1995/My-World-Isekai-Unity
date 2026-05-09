using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Applies a configurable CharacterStatusEffect to the receiver on disobedience.
    /// Works with null issuer (anonymous orders, e.g., a "no trespassing" sign applying a Wanted status).
    ///
    /// The asset field uses CharacterStatusEffect — the project's canonical SO type for status effects.
    /// The caster parameter is passed as the issuer's Character (or null for anonymous orders)
    /// to support personality-based compatibility modifiers downstream.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Consequences/Status Effect", fileName = "Consequence_StatusEffect_New")]
    public class Consequence_StatusEffect : ScriptableObject, IOrderConsequence
    {
        [Tooltip("Status effect to apply to the receiver on disobedience.")]
        [SerializeField] private CharacterStatusEffect _statusEffect;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (receiver == null || _statusEffect == null) return;

            if (receiver.StatusManager == null)
            {
                Debug.LogWarning($"<color=yellow>[Consequence_StatusEffect]</color> Receiver {receiver.CharacterName} has no StatusManager; skipping.");
                return;
            }

            // ApplyEffect(CharacterStatusEffect, Character caster) is the canonical entry point.
            receiver.StatusManager.ApplyEffect(_statusEffect, issuer?.AsCharacter);

            Debug.Log($"<color=red>[Order]</color> {receiver.CharacterName} gains status '{_statusEffect.StatusEffectName}' for disobedience.");
        }
    }
}
