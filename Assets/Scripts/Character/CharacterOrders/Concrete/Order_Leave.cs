// Assets/Scripts/Character/CharacterOrders/Concrete/Order_Leave.cs
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWI.Orders
{
    /// <summary>
    /// "Leave this area within N seconds." Compliance = receiver outside the sphere
    /// defined by (zoneCenter, zoneRadius). Optional zoneEntityId references an
    /// IWorldZone for richer integration; if 0, the order is a free-floating sphere.
    /// </summary>
    public class Order_Leave : OrderImmediate
    {
        public Vector3 ZoneCenter;
        public float   ZoneRadius;
        public ulong   ZoneEntityId;          // 0 if not tied to a specific IWorldZone

        public override bool CanIssueAgainst(Character receiver)
        {
            if (receiver == null) return false;
            return Vector3.Distance(receiver.transform.position, ZoneCenter) <= ZoneRadius;
        }

        public override bool IsComplied()
        {
            if (Receiver == null) return true; // dead/despawned receivers are "compliant"
            return Vector3.Distance(Receiver.transform.position, ZoneCenter) > ZoneRadius;
        }

        public override Dictionary<string, object> GetGoapPrecondition()
        {
            // GOAP key the planner looks at to know "agent must be outside zone X".
            // Goap_MoveToPosition (or equivalent) should satisfy this when the world-state
            // evaluator computes distance-from-center against the receiver's position.
            return new Dictionary<string, object>
            {
                { $"OutsideZone_{ZoneEntityId}", true }
            };
        }

        public override byte[] SerializeOrderPayload()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(ZoneCenter.x);
            bw.Write(ZoneCenter.y);
            bw.Write(ZoneCenter.z);
            bw.Write(ZoneRadius);
            bw.Write(ZoneEntityId);
            return ms.ToArray();
        }

        public override void DeserializeOrderPayload(byte[] data)
        {
            if (data == null || data.Length < 24) return;
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            float x = br.ReadSingle();
            float y = br.ReadSingle();
            float z = br.ReadSingle();
            ZoneCenter   = new Vector3(x, y, z);
            ZoneRadius   = br.ReadSingle();
            ZoneEntityId = br.ReadUInt64();
        }
    }
}
