// Assets/Scripts/Character/CharacterOrders/AuthorityContextSO.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Defines a relationship "kind" (Stranger, Friend, Employer, Captain, …) with the
    /// base priority an Order from this kind of issuer carries. Resolved at issue time
    /// by AuthorityResolver from the receiver's existing systems (CharacterJob,
    /// CharacterParty, CharacterRelation, future Family/Faction).
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Orders/Authority Context", fileName = "Authority_New")]
    public class AuthorityContextSO : ScriptableObject
    {
        [Tooltip("Stable name used as the network identifier. Should match the asset filename suffix (e.g., 'Captain' for Authority_Captain).")]
        [SerializeField] private string _contextName;

        [Tooltip("Base priority (0–100) that orders carrying this context start with. Urgency adds on top.")]
        [Range(0, 100)] [SerializeField] private int _basePriority;

        [Tooltip("If true, this context can issue orders without proximity. v1: always false. Future feature.")]
        [SerializeField] private bool _bypassProximity;

        public string ContextName     => _contextName;
        public int    BasePriority    => _basePriority;
        public bool   BypassProximity => _bypassProximity;
    }
}
