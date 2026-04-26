using System.Collections.Generic;
using System.IO;
using MWI.Quests;
using Unity.Netcode;

namespace MWI.Orders
{
    /// <summary>
    /// "Kill target X within N seconds." OrderQuest subclass — appears in the receiver's
    /// quest log with a CharacterTarget pointing at the victim. Compliance = target dead.
    /// </summary>
    public class Order_Kill : OrderQuest
    {
        public ulong TargetCharacterNetId;

        private Character _resolvedTarget;
        private CharacterTarget _questTarget;

        private Character ResolveTargetCharacter()
        {
            if (_resolvedTarget != null) return _resolvedTarget;
            if (TargetCharacterNetId == 0) return null;
            if (NetworkManager.Singleton == null) return null;
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(TargetCharacterNetId, out var obj))
            {
                _resolvedTarget = obj.GetComponent<Character>();
            }
            return _resolvedTarget;
        }

        public override bool CanIssueAgainst(Character receiver)
        {
            var target = ResolveTargetCharacter();
            if (receiver == null || target == null) return false;
            if (target == receiver) return false;
            if (Issuer != null && Issuer.AsCharacter == target) return false;
            if (!target.IsAlive()) return false;
            if (receiver.CharacterCombat == null) return false;
            return true;
        }

        public override bool IsCompleted()
        {
            var target = ResolveTargetCharacter();
            return target == null || !target.IsAlive();
        }

        public override string Title
            => $"Kill {ResolveTargetCharacter()?.CharacterName ?? "<unknown>"}";

        public override string Description
            => $"{Issuer?.DisplayName ?? "Someone"} has ordered you to kill {ResolveTargetCharacter()?.CharacterName ?? "<unknown>"} within {TimeoutSeconds:F0} seconds.";

        public override IQuestTarget Target
        {
            get
            {
                var t = ResolveTargetCharacter();
                if (t == null) return null;
                if (_questTarget == null) _questTarget = new CharacterTarget(t);
                return _questTarget;
            }
        }

        public override Dictionary<string, object> GetGoapPrecondition()
        {
            return new Dictionary<string, object>
            {
                { $"TargetIsDead_{TargetCharacterNetId}", true }
            };
        }

        public override byte[] SerializeOrderPayload()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(TargetCharacterNetId);
            return ms.ToArray();
        }

        public override void DeserializeOrderPayload(byte[] data)
        {
            if (data == null || data.Length < 8) return;
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            TargetCharacterNetId = br.ReadUInt64();
            _resolvedTarget = null;     // Force re-resolve
            _questTarget = null;
        }
    }
}

