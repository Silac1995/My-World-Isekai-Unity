using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Increases the issuer's opinion of the receiver by a fixed amount.
    /// No-ops if issuer is null or issuer has no CharacterRelation.
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Rewards/Relation Gain", fileName = "Reward_RelationGain_New")]
    public class Reward_RelationGain : ScriptableObject, IOrderReward
    {
        [Tooltip("How much the issuer's opinion of the receiver gains. Positive number.")]
        [SerializeField] private int _amount = 10;

        public string SoName => name;

        public void Apply(Order order, Character receiver, IOrderIssuer issuer)
        {
            if (issuer == null || issuer.AsCharacter == null) return;
            if (receiver == null) return;

            var issuerCharacter = issuer.AsCharacter;
            if (issuerCharacter.CharacterRelation == null)
            {
                Debug.LogWarning($"<color=yellow>[Reward_RelationGain]</color> Issuer {issuerCharacter.CharacterName} has no CharacterRelation; skipping.");
                return;
            }

            // UpdateRelation is server-only; rewards are always called server-side.
            issuerCharacter.CharacterRelation.UpdateRelation(receiver, +_amount);
            Debug.Log($"<color=green>[Order]</color> {receiver.CharacterName} obeyed {issuerCharacter.CharacterName}: relation +{_amount}");
        }
    }
}
