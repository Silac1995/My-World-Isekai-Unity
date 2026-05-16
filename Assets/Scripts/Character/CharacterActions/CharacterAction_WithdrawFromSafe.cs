using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side atomic transfer: debits <c>_amount</c> of <c>_currency</c> from the target
/// <see cref="SafeFurniture"/> and credits the character's <see cref="CharacterWallet"/>.
/// Pairs with <see cref="CharacterAction_DepositToSafe"/> for the inverse direction.
///
/// <para>Today queued by <see cref="SafeFurnitureNetworkSync"/>'s withdraw ServerRpc
/// (player UI path). The same action will be queued by future NPC banker / treasurer
/// AI (rule #22 player↔NPC parity).</para>
///
/// <para><b>Atomic guard:</b> the safe debit runs first via <see cref="SafeFurniture.TryDebit"/>.
/// If it returns false (insufficient safe balance), the wallet is NOT credited and a failure
/// notification is routed through the safe's <see cref="SafeFurnitureNetworkSync.NotifyOperationResult"/>
/// dispatcher.</para>
///
/// <para>Single-shot, <c>Duration = 0</c>. Server-only path (early-exit on non-server).</para>
/// </summary>
public sealed class CharacterAction_WithdrawFromSafe : CharacterAction
{
    private readonly SafeFurniture _safe;
    private readonly CurrencyId _currency;
    private readonly int _amount;

    public SafeFurniture Safe => _safe;
    public CurrencyId Currency => _currency;
    public int Amount => _amount;

    public CharacterAction_WithdrawFromSafe(Character character, SafeFurniture safe, CurrencyId currency, int amount)
        : base(character, 0f)
    {
        _safe = safe;
        _currency = currency;
        _amount = amount;
    }

    public override bool CanExecute()
    {
        if (character == null || _safe == null) return false;
        if (_amount <= 0) return false;
        if (character.CharacterWallet == null) return false;
        // Safe sufficiency re-checked atomically in OnApplyEffect via TryDebit; CanExecute
        // is best-effort (balance can shift between queue + execute).
        return _safe.CanAfford(_currency, _amount);
    }

    public override void OnStart()
    {
        // No animation trigger — withdraw is a discrete UI-driven action. Visual feedback
        // lives on the safe (OnBalanceChanged → UI repaint) and the wallet (broadcast).
    }

    public override void OnApplyEffect()
    {
        // Server-only path (early-exit on non-server). Queued only via
        // SafeFurnitureNetworkSync.RequestWithdrawServerRpc in Task 4, which is server-side.
        // The explicit IsServer guard makes the action safe-by-construction regardless of
        // how it's queued (defense in depth — Duration <= 0 actions otherwise run on both
        // server and remote clients per CharacterActions.cs:78-94).
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (_safe == null || _amount <= 0 || character == null) return;

        var wallet = character.CharacterWallet;
        if (wallet == null)
        {
            if (NPCDebug.VerboseActions)
                Debug.LogWarning($"<color=orange>[WithdrawFromSafe]</color> {character.CharacterName} aborted: no CharacterWallet.");
            return;
        }

        // Atomic: safe first, then wallet. If the safe debit fails, do NOT credit wallet.
        if (!_safe.TryDebit(_currency, _amount, "player-withdraw"))
        {
            // Insufficient safe balance. Route failure feedback to the requesting client through
            // the safe's NetSync dispatcher (Task 4 fleshes out the ClientRpc body).
            _safe.NetSync?.NotifyOperationResult(
                character.OwnerClientId,
                success: false,
                reason: "insufficient-safe");
            if (NPCDebug.VerboseActions)
                Debug.LogWarning($"<color=orange>[WithdrawFromSafe]</color> {character.CharacterName} insufficient safe balance for {_amount} of {_currency} → wallet not credited.");
            return;
        }

        wallet.AddCoins(_currency, _amount, "safe-withdraw");
        if (NPCDebug.VerboseActions)
            Debug.Log($"<color=green>[WithdrawFromSafe]</color> {character.CharacterName} withdrew {_amount} of {_currency} from {_safe.FurnitureName}.");
    }
}
