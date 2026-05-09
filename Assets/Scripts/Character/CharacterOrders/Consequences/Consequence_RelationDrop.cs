using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Reduces the issuer's relation toward the receiver by a fixed amount.
    /// No-ops if issuer is null (anonymous order) or issuer has no CharacterRelation.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Consequences/Relation Drop", fileName = "Consequence_RelationDrop_New")]
    public class Consequence_RelationDrop : ScriptableObject, IOrderConsequence
    {
        [Tooltip("How much the issuer's opinion of the receiver drops. Positive number = larger drop.")]
        [SerializeField] private int _amount = 10;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (issuer == null || issuer.AsCharacter == null) return;
            if (receiver == null) return;

            var issuerCharacter = issuer.AsCharacter;
            if (issuerCharacter.CharacterRelation == null)
            {
                Debug.LogWarning($"<color=yellow>[Consequence_RelationDrop]</color> Issuer {issuerCharacter.CharacterName} has no CharacterRelation; skipping.");
                return;
            }

            // UpdateRelation is server-only; consequences are always called server-side.
            issuerCharacter.CharacterRelation.UpdateRelation(receiver, -_amount);
            Debug.Log($"<color=red>[Order]</color> {receiver.CharacterName} disobeyed {issuerCharacter.CharacterName}: relation -{_amount}");
        }
    }
}
