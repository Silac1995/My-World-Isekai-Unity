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
        public ulong        receiverCharacterId;       // Character.CharacterId, NOT NetworkObjectId
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
