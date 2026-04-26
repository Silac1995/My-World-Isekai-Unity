// Assets/Scripts/Character/CharacterActions/CharacterAction_IssueOrder.cs
using Unity.Collections;
using UnityEngine;
using MWI.Orders;

/// <summary>
/// CharacterAction wrapper that issues an order from this Character to a target.
/// Per rule #22, all gameplay (player and NPC) routes through CharacterAction.
/// Player HUD enqueues this action; NPC GOAP enqueues the same action.
///
/// The action calls IssueOrderServerRpc on the owner's CharacterOrders subsystem.
/// SO name arrays for consequences/rewards are packed as pipe-delimited
/// FixedString512Bytes because NGO does not allow string[] in RPC signatures.
/// </summary>
public class CharacterAction_IssueOrder : CharacterAction
{
    private readonly Character    _target;
    private readonly string       _orderTypeName;
    private readonly OrderUrgency _urgency;
    private readonly byte[]       _payload;
    private readonly string[]     _consequenceSoNames;
    private readonly string[]     _rewardSoNames;
    private readonly float        _timeoutSeconds;

    public override string ActionName => $"Issue {_orderTypeName}";

    /// <param name="character">The issuer (this Character).</param>
    /// <param name="target">The receiver Character.</param>
    /// <param name="orderTypeName">Must match a key registered in <see cref="OrderFactory"/>.</param>
    /// <param name="urgency">Urgency modifier applied on top of AuthorityContext.BasePriority.</param>
    /// <param name="payload">Optional serialized order-specific data (≤62 bytes; larger payloads are truncated server-side with a warning).</param>
    /// <param name="consequenceSoNames">Names of OrderConsequence SO assets under Resources/Data/OrderConsequences/. Null-safe.</param>
    /// <param name="rewardSoNames">Names of OrderReward SO assets under Resources/Data/OrderRewards/. Null-safe.</param>
    /// <param name="timeoutSeconds">How long the receiver has to respond before auto-refuse.</param>
    public CharacterAction_IssueOrder(
        Character    character,
        Character    target,
        string       orderTypeName,
        OrderUrgency urgency,
        byte[]       payload,
        string[]     consequenceSoNames,
        string[]     rewardSoNames,
        float        timeoutSeconds)
        : base(character, duration: 0.5f)
    {
        _target             = target;
        _orderTypeName      = orderTypeName;
        _urgency            = urgency;
        _payload            = payload ?? System.Array.Empty<byte>();
        _consequenceSoNames = consequenceSoNames ?? System.Array.Empty<string>();
        _rewardSoNames      = rewardSoNames      ?? System.Array.Empty<string>();
        _timeoutSeconds     = timeoutSeconds;
    }

    public override bool CanExecute()
    {
        if (_target == null || !_target.IsAlive())
        {
            Debug.Log($"[CharacterAction_IssueOrder] CanExecute false: target is null or dead.");
            return false;
        }
        if (_target.CharacterInteractable == null)
        {
            Debug.Log($"[CharacterAction_IssueOrder] CanExecute false: target {_target.CharacterName} has no CharacterInteractable.");
            return false;
        }
        // Per feedback_interactable_proximity_api.md — use IsCharacterInInteractionZone, not bounds math.
        if (!_target.CharacterInteractable.IsCharacterInInteractionZone(character))
        {
            Debug.Log($"[CharacterAction_IssueOrder] CanExecute false: {character.CharacterName} not in interaction zone of {_target.CharacterName}.");
            return false;
        }
        if (character.CharacterOrders == null)
        {
            Debug.LogError($"[CharacterAction_IssueOrder] CanExecute false: issuer {character.CharacterName} has no CharacterOrders subsystem.");
            return false;
        }
        return true;
    }

    public override void OnStart()
    {
        // No dedicated animation for v1 — order issuance is instant.
        // Future: trigger a "command gesture" animation here.
    }

    public override void OnApplyEffect()
    {
        ulong receiverNetId = _target.NetworkObject != null ? _target.NetworkObject.NetworkObjectId : 0;

        // FixedString64Bytes: order type name (max 63 UTF-8 bytes; warn if longer).
        if (System.Text.Encoding.UTF8.GetByteCount(_orderTypeName) > 63)
        {
            Debug.LogWarning($"[CharacterAction_IssueOrder] orderTypeName '{_orderTypeName}' exceeds 63 UTF-8 bytes; it will be truncated by FixedString64Bytes.");
        }
        var typeName = new FixedString64Bytes(_orderTypeName);

        // Pack SO name arrays as pipe-delimited FixedString512Bytes — NGO rejects string[] in RPCs.
        var consequencesPacked = new FixedString512Bytes(string.Join("|", _consequenceSoNames));
        var rewardsPacked      = new FixedString512Bytes(string.Join("|", _rewardSoNames));

        character.CharacterOrders.IssueOrderServerRpc(
            receiverNetId,
            typeName,
            (byte)_urgency,
            _payload,
            consequencesPacked,
            rewardsPacked,
            _timeoutSeconds);

        Finish();
    }
}
