using UnityEngine;
using MWI.Orders;

/// <summary>
/// Dev-only: press F9 (Host only) to issue an Order_Leave to the nearest NPC,
/// telling them to leave a 5-unit sphere centered on the NPC's current position.
/// Used to validate the full Order pipeline manually. Remove after Phase 4.
///
/// USAGE:
///   1. Drop this MonoBehaviour onto any GameObject in the dev test scene.
///   2. Drag the Host player Character into _hostPlayer.
///   3. Enter Play mode (Host or Host+Client). Stand near an NPC. Press F9.
///   4. Watch the Console for [Order] log lines.
///
/// Expected outcome (NPC accepts):
///   [Order] {NPC} evaluates Order_Leave from {Host} (P=...): score=... → ACCEPTED
///   {NPC walks out of the sphere}
///   [Order] {NPC} obeyed {Host}: relation +0  (no reward configured for Order_Leave)
///
/// Expected outcome (NPC refuses, e.g., enemy or low loyalty):
///   [Order] {NPC} evaluates Order_Leave from {Host} (P=...): score=... → REFUSED
///   [Order] {NPC} disobeyed {Host}: relation -10
///   [Order] {Host} now attacks {NPC} for disobedience.
/// </summary>
public class DevOrderLeaveTester : MonoBehaviour
{
    [SerializeField] private Character _hostPlayer;
    [SerializeField] private float _orderTimeout = 15f;
    [SerializeField] private float _zoneRadius   = 5f;

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.F9)) return;
        if (_hostPlayer == null)
        {
            Debug.LogWarning("[DevOrderLeaveTester] _hostPlayer not assigned.");
            return;
        }
        if (_hostPlayer.CharacterOrders == null)
        {
            Debug.LogWarning("[DevOrderLeaveTester] Host player has no CharacterOrders subsystem.");
            return;
        }

        Character nearest = null;
        float bestDist = float.MaxValue;
        foreach (var c in FindObjectsOfType<Character>())
        {
            if (c == _hostPlayer) continue;
            if (c.IsPlayer()) continue;
            if (!c.IsAlive()) continue;
            float d = Vector3.Distance(_hostPlayer.transform.position, c.transform.position);
            if (d < bestDist) { bestDist = d; nearest = c; }
        }
        if (nearest == null)
        {
            Debug.LogWarning("[DevOrderLeaveTester] No NPC nearby.");
            return;
        }

        // Build Order_Leave payload manually (matches Order_Leave.SerializeOrderPayload format).
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);
        Vector3 c0 = nearest.transform.position;
        bw.Write(c0.x); bw.Write(c0.y); bw.Write(c0.z);
        bw.Write(_zoneRadius);
        bw.Write((ulong)0); // ZoneEntityId 0 = free-floating sphere
        byte[] payload = ms.ToArray();

        // Pack consequence SO names as pipe-delimited (NGO doesn't allow string[] in RPCs).
        string consequencesPacked = "Consequence_RelationDrop_Light|Consequence_IssuerAttacks";
        string rewardsPacked = "";

        _hostPlayer.CharacterOrders.IssueOrderServerRpc(
            nearest.NetworkObject.NetworkObjectId,
            new Unity.Collections.FixedString64Bytes("Order_Leave"),
            (byte)OrderUrgency.Urgent,
            payload,
            new Unity.Collections.FixedString512Bytes(consequencesPacked),
            new Unity.Collections.FixedString512Bytes(rewardsPacked),
            _orderTimeout);

        Debug.Log($"[DevOrderLeaveTester] Issued Order_Leave to {nearest.CharacterName} (zone={c0}, r={_zoneRadius}).");
    }
}
