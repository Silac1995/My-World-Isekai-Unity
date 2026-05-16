using System.Collections.Generic;
using MWI.Economy;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative replication for a <see cref="SafeFurniture"/> sibling.
/// Mirrors the <see cref="StorageFurnitureNetworkSync"/> pattern exactly so the
/// pair behaves identically from the perspective of the management UI, the
/// role-mutator pipeline, and the save/load layer.
///
/// <para>
/// Two replicated fields:
///   1. <see cref="_networkRole"/> — per-safe role (Treasury / None) for the
///      building Treasury aggregator and the management panel dropdown.
///   2. <see cref="_networkBalances"/> — per-<c>CurrencyId</c> coin amounts
///      that constitute this safe's slice of the building treasury.
/// </para>
///
/// Authored 2026-05-09 as part of the B2B shop-buy logistics path.
/// </summary>
[RequireComponent(typeof(SafeFurniture))]
[RequireComponent(typeof(NetworkObject))]
public class SafeFurnitureNetworkSync : NetworkBehaviour
{
    private SafeFurniture _safe;

    /// <summary>
    /// Server-write per-safe role. Default = <see cref="SafeRoleType.None"/>.
    /// First-spawn seeded from <see cref="SafeFurniture.InitialRole"/>. Owner-driven
    /// mutations route through <see cref="SetRoleServer"/>; the NPC LogisticsManager's
    /// shift-punch auto-assign uses the same entry point.
    /// </summary>
    private NetworkVariable<SafeRoleType> _networkRole;

    /// <summary>
    /// Server-write per-currency balance snapshot. Server rebuilds the full
    /// list every time <see cref="_safe"/> fires <see cref="SafeFurniture.OnBalanceChanged"/>
    /// (mirrors <c>StorageFurnitureNetworkSync</c>'s clear+re-add approach — the
    /// list is tiny, at most one entry per active currency).
    /// </summary>
    private NetworkList<BuildingTreasuryEntry> _networkBalances;

    private void Awake()
    {
        _safe = GetComponent<SafeFurniture>();
        // NetworkList must exist before OnNetworkSpawn (matches the pattern on
        // every other NetworkList-bearing component in the project).
        _networkRole = new NetworkVariable<SafeRoleType>(
            SafeRoleType.None,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        _networkBalances = new NetworkList<BuildingTreasuryEntry>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (_safe == null)
        {
            Debug.LogError($"<color=red>[SafeFurnitureNetworkSync]</color> {name}: missing SafeFurniture sibling — sync layer cannot run.");
            return;
        }

        if (IsServer)
        {
            // Subscribe to source-of-truth events BEFORE seeding so any mutation
            // landing during/after this frame propagates.
            _safe.OnBalanceChanged += HandleServerBalanceChanged;

            // Seed role from Inspector if the NetworkVariable is still default.
            // Save-load runs AFTER OnNetworkSpawn — when a save entry has a
            // non-default role, MapController.RestoreSafeFurnitureContents writes
            // it through SetRoleServer which overwrites this seed. So this line
            // is safe whether or not a save is being applied.
            if (_networkRole.Value == SafeRoleType.None)
            {
                _networkRole.Value = _safe.InitialRole;
            }
            // Mirror the seed into the local SafeFurniture so server-side reads
            // of safe.Role return the right value before any client connects.
            _safe.ApplyRoleFromNetwork(_networkRole.Value);

            // Seed balance from Inspector if the network list is empty (no save
            // restore has run). Walks _initialBalances → SafeFurniture's
            // RestoreFromSaveData → fires OnBalanceChanged → our handler
            // rebuilds the NetworkList for replication.
            if (_networkBalances.Count == 0 && _safe.InitialBalances != null && _safe.InitialBalances.Count > 0)
            {
                _safe.RestoreFromSaveData(_safe.InitialBalances);
            }

            // Server-side role subscription so any server-driven role change
            // (RPC + auto-assign + save-restore) fires the SafeFurniture's
            // OnRoleChanged on the host too.
            _networkRole.OnValueChanged += HandleRoleChanged;
        }
        else
        {
            // Client-side: subscribe to both NetworkVariable + NetworkList,
            // then run an explicit catch-up pass for late-joiner safety.
            _networkRole.OnValueChanged += HandleRoleChanged;
            _safe.ApplyRoleFromNetwork(_networkRole.Value);

            _networkBalances.OnListChanged += HandleClientBalancesChanged;
            ApplyFullBalancesOnClient();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_safe != null)
        {
            _safe.OnBalanceChanged -= HandleServerBalanceChanged;
        }
        if (_networkRole != null)
        {
            _networkRole.OnValueChanged -= HandleRoleChanged;
        }
        if (_networkBalances != null && !IsServer)
        {
            _networkBalances.OnListChanged -= HandleClientBalancesChanged;
        }
        base.OnNetworkDespawn();
    }

    // ──────────────────────────────────────────────────────────────────
    // Server-only role mutator. The single entry point used by both the
    // player UI's ServerRpc-driven update and the NPC auto-assign pass.
    // ──────────────────────────────────────────────────────────────────

    public void SetRoleServer(SafeRoleType newRole)
    {
        if (!IsServer || _networkRole == null) return;
        if (_networkRole.Value == newRole) return;
        _networkRole.Value = newRole;
    }

    // ──────────────────────────────────────────────────────────────────
    // Player UI: deposit/withdraw request RPCs (client → server).
    //
    // Mirrors the dispatch shape used by CashierNetSync.RequestStartBuyServerRpc /
    // RequestUseCashierServerRpc — the only sibling that also (a) extracts the
    // sender id, (b) validates character ownership server-side, (c) re-validates
    // interaction-zone proximity as anti-cheat (rule #36), and (d) fires a
    // targeted ClientRpc back to the requester on failure.
    //
    // Wallet-balance checks live INSIDE the CharacterAction (insufficient-wallet /
    // insufficient-safe) — those failure reasons are emitted by the action and
    // routed through NotifyOperationResult below. The RPC body only owns
    // out-of-zone and invalid-amount.
    // ──────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void RequestDepositServerRpc(NetworkBehaviourReference characterRef, int currencyRawId, int amount, ServerRpcParams p = default)
    {
        if (!IsServer) return;

        // 1. Resolve the character that issued the request.
        if (!characterRef.TryGet(out Character character) || character == null)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning("[SafeFurnitureNetworkSync] RequestDepositServerRpc: character ref failed to resolve.");
            return;
        }

        // 2. Anti-cheat: the sender must own the character they claim to be.
        ulong senderClientId = p.Receive.SenderClientId;
        if (character.OwnerClientId != senderClientId)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestDepositServerRpc: sender {senderClientId} does not own character (owned by {character.OwnerClientId}).");
            return;
        }

        // 3. Anti-cheat: proximity re-validation (rule #36 — never trust the client).
        if (_safe == null)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning("[SafeFurnitureNetworkSync] RequestDepositServerRpc: _safe sibling missing — cannot validate proximity.");
            return;
        }
        var interactable = _safe.GetComponent<InteractableObject>();
        if (interactable == null || !interactable.IsCharacterInInteractionZone(character))
        {
            NotifyOperationResult(senderClientId, success: false, reason: "out-of-zone");
            return;
        }

        // 4. Amount sanity (negative + zero rejected here, wallet sufficiency lives in the action).
        if (amount <= 0)
        {
            NotifyOperationResult(senderClientId, success: false, reason: "invalid-amount");
            return;
        }

        // 5. Queue the server-side action. Insufficient-wallet feedback fires from
        // inside the action via this same NotifyOperationResult path.
        if (character.CharacterActions == null)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestDepositServerRpc: character {character.CharacterName} has no CharacterActions.");
            return;
        }

        var currency = new CurrencyId(currencyRawId);
        character.CharacterActions.ExecuteAction(new CharacterAction_DepositToSafe(character, _safe, currency, amount));
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestWithdrawServerRpc(NetworkBehaviourReference characterRef, int currencyRawId, int amount, ServerRpcParams p = default)
    {
        if (!IsServer) return;

        // 1. Resolve the character that issued the request.
        if (!characterRef.TryGet(out Character character) || character == null)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning("[SafeFurnitureNetworkSync] RequestWithdrawServerRpc: character ref failed to resolve.");
            return;
        }

        // 2. Anti-cheat: the sender must own the character they claim to be.
        ulong senderClientId = p.Receive.SenderClientId;
        if (character.OwnerClientId != senderClientId)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestWithdrawServerRpc: sender {senderClientId} does not own character (owned by {character.OwnerClientId}).");
            return;
        }

        // 3. Anti-cheat: proximity re-validation (rule #36 — never trust the client).
        if (_safe == null)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning("[SafeFurnitureNetworkSync] RequestWithdrawServerRpc: _safe sibling missing — cannot validate proximity.");
            return;
        }
        var interactable = _safe.GetComponent<InteractableObject>();
        if (interactable == null || !interactable.IsCharacterInInteractionZone(character))
        {
            NotifyOperationResult(senderClientId, success: false, reason: "out-of-zone");
            return;
        }

        // 4. Amount sanity.
        if (amount <= 0)
        {
            NotifyOperationResult(senderClientId, success: false, reason: "invalid-amount");
            return;
        }

        // 5. Queue the server-side action. Insufficient-safe feedback fires from
        // inside the action via this same NotifyOperationResult path.
        if (character.CharacterActions == null)
        {
            if (Debug.isDebugBuild)
                Debug.LogWarning($"[SafeFurnitureNetworkSync] RequestWithdrawServerRpc: character {character.CharacterName} has no CharacterActions.");
            return;
        }

        var currency = new CurrencyId(currencyRawId);
        character.CharacterActions.ExecuteAction(new CharacterAction_WithdrawFromSafe(character, _safe, currency, amount));
    }

    // ──────────────────────────────────────────────────────────────────
    // Per-client deposit/withdraw operation feedback (player UI).
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only entry point used by <c>CharacterAction_DepositToSafe</c> /
    /// <c>CharacterAction_WithdrawFromSafe</c> and by the request RPCs above to
    /// report success / failure to the requesting client. Fires a targeted
    /// <see cref="OperationResultClientRpc"/> that lands on exactly one peer —
    /// the one that owns the character that initiated the operation. The reason
    /// string is converted to a <see cref="FixedString64Bytes"/> at this
    /// boundary so action call sites can keep passing C# string literals.
    ///
    /// <para><b>Wire reason values (spec §9):</b>
    /// <c>"insufficient-wallet"</c>, <c>"insufficient-safe"</c>,
    /// <c>"out-of-zone"</c>, <c>"invalid-amount"</c>.</para>
    /// </summary>
    public void NotifyOperationResult(ulong targetClientId, bool success, string reason)
    {
        if (!IsServer) return;
        var fsReason = new FixedString64Bytes(reason ?? string.Empty);
        var rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } }
        };
        OperationResultClientRpc(success, fsReason, rpcParams);
    }

    [ClientRpc]
    private void OperationResultClientRpc(bool success, FixedString64Bytes reason, ClientRpcParams rpcParams = default)
    {
        // Targeted to the requester only — fires on exactly one client.
        // PlayerUI.Instance is the safe entry point; Task 7 fleshes out the
        // body of OnSafeOperationResult (toast + inline panel feedback).
        if (PlayerUI.Instance != null)
        {
            PlayerUI.Instance.OnSafeOperationResult(_safe, success, reason.ToString());
        }
    }

    private void HandleRoleChanged(SafeRoleType _, SafeRoleType newValue)
    {
        if (_safe != null) _safe.ApplyRoleFromNetwork(newValue);
    }

    // ──────────────────────────────────────────────────────────────────
    // Balance replication. Server rebuilds the list on each
    // OnBalanceChanged fired by the SafeFurniture. Clients mirror via
    // OnListChanged → ApplyBalancesFromNetwork.
    // ──────────────────────────────────────────────────────────────────

    private void HandleServerBalanceChanged()
    {
        if (!IsServer || _networkBalances == null || _safe == null) return;
        _networkBalances.Clear();
        var snapshot = _safe.Balances;
        for (int i = 0; i < snapshot.Count; i++)
        {
            _networkBalances.Add(snapshot[i]);
        }
    }

    private void HandleClientBalancesChanged(NetworkListEvent<BuildingTreasuryEntry> _)
    {
        ApplyFullBalancesOnClient();
    }

    private static readonly List<BuildingTreasuryEntry> _scratchClientBalances = new List<BuildingTreasuryEntry>(4);

    private void ApplyFullBalancesOnClient()
    {
        if (_networkBalances == null || _safe == null) return;
        _scratchClientBalances.Clear();
        for (int i = 0; i < _networkBalances.Count; i++)
        {
            _scratchClientBalances.Add(_networkBalances[i]);
        }
        _safe.ApplyBalancesFromNetwork(_scratchClientBalances);
    }
}
