// Assets/Scripts/Character/CharacterOrders/OrdersSaveData.cs
using System;
using System.Collections.Generic;

namespace MWI.Orders
{
    /// <summary>
    /// Persistence root for CharacterOrders. Saves only the issuer-side ledger.
    ///   - Receiver-side OrderQuests are persisted by CharacterQuestLog (they implement IQuest).
    ///   - Receiver-side OrderImmediates are intentionally transient.
    /// See spec §7.
    /// </summary>
    [Serializable]
    public class OrdersSaveData
    {
        /// <summary>Outstanding orders this character has issued and is waiting on.</summary>
        public List<IssuedOrderSaveEntry> issuedOrders = new();
    }

    [Serializable]
    public class IssuedOrderSaveEntry
    {
        // Stable hash of the receiver's GUID — kept for diagnostics / future use.
        // (Character.CharacterId is a Guid string; we hash it for the ulong field but use the
        //  string version for FindByUUID resolution on reload.)
        public ulong        receiverCharacterId;
        // The actual GUID string used to resolve via Character.FindByUUID across save/load.
        public string       receiverCharacterIdString;
        public string       orderTypeName;
        public string       authorityContextName;
        public byte         urgency;
        public float        timeoutRemaining;
        public byte[]       orderPayload;
        public bool         isQuestBacked;
        public ulong        linkedQuestId;
        public List<string> consequenceSoNames = new();
        public List<string> rewardSoNames      = new();
    }
}
