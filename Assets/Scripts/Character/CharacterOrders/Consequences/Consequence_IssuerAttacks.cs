using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// The issuer initiates combat against the receiver by setting them as the planned target.
    /// Uses CharacterCombat.SetPlannedTarget — the canonical single entry point for all target changes.
    /// No-ops if issuer is null, dead, or has no CharacterCombat.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Consequences/Issuer Attacks", fileName = "Consequence_IssuerAttacks")]
    public class Consequence_IssuerAttacks : ScriptableObject, IOrderConsequence
    {
        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (issuer == null || issuer.AsCharacter == null) return;

            var issuerCharacter = issuer.AsCharacter;

            if (!issuerCharacter.IsAlive())
            {
                Debug.Log($"<color=orange>[Consequence_IssuerAttacks]</color> Issuer {issuerCharacter.CharacterName} is dead; cannot retaliate.");
                return;
            }

            if (issuerCharacter.CharacterCombat == null)
            {
                Debug.LogWarning($"<color=yellow>[Consequence_IssuerAttacks]</color> Issuer {issuerCharacter.CharacterName} has no CharacterCombat; skipping.");
                return;
            }

            if (receiver == null || !receiver.IsAlive())
            {
                Debug.Log($"<color=orange>[Consequence_IssuerAttacks]</color> Receiver is null or dead; skipping.");
                return;
            }

            // SetPlannedTarget is the canonical entry point for all target changes:
            // updates look target, targeting graph, evaluates engagements, and triggers movement.
            issuerCharacter.CharacterCombat.SetPlannedTarget(receiver);

            Debug.Log($"<color=red>[Order]</color> {issuerCharacter.CharacterName} now targets {receiver.CharacterName} for disobedience.");
        }
    }
}
