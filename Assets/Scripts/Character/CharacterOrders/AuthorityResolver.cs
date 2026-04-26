// Assets/Scripts/Character/CharacterOrders/AuthorityResolver.cs
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// Server-only, stateless static helper. Given an issuer + receiver, derives the
    /// highest-applying AuthorityContextSO from the receiver's existing systems.
    ///
    /// Resolution order (highest BasePriority wins on tie — but in practice each
    /// context kind is mutually exclusive for a given (issuer, receiver) pair):
    ///   1. Lord       — TODO future Faction integration
    ///   2. Captain    — TODO future Faction integration
    ///   3. Employer   — receiver.CharacterJob.Workplace.Owner == issuer
    ///   4. PartyLeader — receiver.CharacterParty.PartyData.IsLeader(issuer.CharacterId)
    ///   5. Parent     — TODO future Family integration
    ///   6. Friend     — receiver.CharacterRelation.IsFriend(issuer)
    ///   7. Stranger   — fallback
    ///
    /// Anonymous issuer (null) always resolves to Stranger.
    /// </summary>
    public static class AuthorityResolver
    {
        // Cached SO refs; loaded lazily from Resources/Data/AuthorityContexts.
        private static AuthorityContextSO _stranger;
        private static AuthorityContextSO _friend;
        private static AuthorityContextSO _parent;
        private static AuthorityContextSO _partyLeader;
        private static AuthorityContextSO _employer;
        private static AuthorityContextSO _captain;
        private static AuthorityContextSO _lord;

        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _stranger    = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Stranger");
            _friend      = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Friend");
            _parent      = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Parent");
            _partyLeader = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_PartyLeader");
            _employer    = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Employer");
            _captain     = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Captain");
            _lord        = Resources.Load<AuthorityContextSO>("Data/AuthorityContexts/Authority_Lord");
            _loaded = true;

            if (_stranger == null)
            {
                Debug.LogError("<color=red>[AuthorityResolver]</color> Authority_Stranger asset missing in Resources/Data/AuthorityContexts/. Resolution will return null and break Order issuance.");
            }
        }

        /// <summary>
        /// Resolve the AuthorityContext to apply when 'issuer' issues an order to 'receiver'.
        /// Pure server-side function. Returns the Stranger SO if no other context matches.
        /// Returns null only if the Stranger asset itself is missing.
        /// </summary>
        public static AuthorityContextSO Resolve(IOrderIssuer issuer, Character receiver)
        {
            EnsureLoaded();

            if (issuer == null || issuer.AsCharacter == null || receiver == null)
            {
                return _stranger;
            }

            Character issuerCharacter = issuer.AsCharacter;

            // 1. Lord — TODO: integrate with future Faction system.
            // 2. Captain — TODO: integrate with future Faction system.

            // 3. Employer
            // API note: CharacterJob has no .Employer property. Employer is derived via
            // receiver.CharacterJob.Workplace (CommercialBuilding) and CommercialBuilding.Owner.
            if (receiver.CharacterJob != null
                && receiver.CharacterJob.Workplace is CommercialBuilding workplace
                && workplace.Owner == issuerCharacter
                && _employer != null)
            {
                return _employer;
            }

            // 4. PartyLeader
            // API note: CharacterParty has no .Leader Character property. Party leadership
            // is stored as LeaderId (string) inside PartyData. We compare via
            // receiver.CharacterParty.PartyData.IsLeader(issuerCharacter.CharacterId).
            if (receiver.CharacterParty != null
                && receiver.CharacterParty.PartyData != null
                && receiver.CharacterParty.PartyData.IsLeader(issuerCharacter.CharacterId)
                && _partyLeader != null)
            {
                return _partyLeader;
            }

            // 5. Parent — TODO: integrate with future Family system.

            // 6. Friend
            if (receiver.CharacterRelation != null
                && receiver.CharacterRelation.IsFriend(issuerCharacter)
                && _friend != null)
            {
                return _friend;
            }

            // 7. Stranger fallback
            return _stranger;
        }

        /// <summary>Test-only reset hook so unit tests can inject mock SOs.</summary>
        internal static void ResetForTests()
        {
            _loaded = false;
            _stranger = _friend = _parent = _partyLeader = _employer = _captain = _lord = null;
        }
    }
}
