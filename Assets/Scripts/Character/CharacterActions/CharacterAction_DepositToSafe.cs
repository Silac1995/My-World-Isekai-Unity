using MWI.Economy;
using UnityEngine;

/// <summary>
/// Server-side atomic transfer: pulls <c>_amount</c> of <c>_currency</c> from the
/// character's <see cref="CharacterWallet"/> and credits the target <see cref="SafeFurniture"/>.
/// Pairs with <c>CharacterAction_WithdrawFromSafe</c> for the inverse direction.
///
/// <para>Today queued by <see cref="SafeFurnitureNetworkSync"/>'s deposit ServerRpc
/// (player UI path). The same action will be queued by future NPC banker / treasurer
/// AI (rule #22 player↔NPC parity) — that's why the mutation lives in a
/// <see cref="CharacterAction"/> instead of being inlined in the RPC.</para>
///
/// <para><b>Atomic guard:</b> the wallet debit runs first. If it returns false
/// (insufficient funds), the safe is NOT credited and a failure notification is
/// routed through the safe's <see cref="SafeFurnitureNetworkSync.NotifyOperationResult"/>
/// ClientRpc dispatcher. This prevents the "double withdraw" hazard where the
/// wallet would be debited but the safe never receives the coins (or vice versa).</para>
///
/// <para>Single-shot, <c>Duration = 0</c>. <see cref="CharacterActions.ExecuteAction"/>
/// runs <see cref="OnStart"/> then <see cref="OnApplyEffect"/> back-to-back and
/// auto-calls <see cref="CharacterAction.Finish"/> for zero-duration actions.</para>
/// </summary>
public sealed class CharacterAction_DepositToSafe : CharacterAction
{
    private readonly SafeFurniture _safe;
    private readonly CurrencyId _currency;
    private readonly int _amount;

    public SafeFurniture Safe => _safe;
    public CurrencyId Currency => _currency;
    public int Amount => _amount;

    public CharacterAction_DepositToSafe(Character character, SafeFurniture safe, CurrencyId currency, int amount)
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
        var wallet = character.CharacterWallet;
        if (wallet == null) return false;
        // Wallet sufficiency is re-checked atomically in OnApplyEffect via RemoveCoins;
        // CanExecute is best-effort (wallet contents can shift between queue + execute).
        return wallet.CanAfford(_currency, _amount);
    }

    public override void OnStart()
    {
        // No animation trigger — deposit is a discreet UI-driven action. Visual feedback
        // lives on the safe (OnBalanceChanged → UI repaint) and the wallet (broadcast).
    }

    public override void OnApplyEffect()
    {
        // Server-only path. CharacterActions.ExecuteAction's instant-action branch routes
        // here for IsServer; remote clients hit the predicted-proxy branch which doesn't
        // mutate authoritative state. Defensive re-check + atomic guard below.
        if (_safe == null || _amount <= 0) return;

        var wallet = character != null ? character.CharacterWallet : null;
        if (wallet == null)
        {
            Debug.LogWarning($"<color=orange>[DepositToSafe]</color> {character?.CharacterName} aborted: no CharacterWallet.");
            return;
        }

        // Atomic: only credit the safe if the wallet debit succeeded.
        if (!wallet.RemoveCoins(_currency, _amount, "safe-deposit"))
        {
            // Insufficient wallet. Route failure feedback to the requesting client through
            // the safe's NetSync dispatcher (Task 4 fleshes out the ClientRpc body).
            _safe.NetSync?.NotifyOperationResult(
                character != null ? character.OwnerClientId : 0UL,
                success: false,
                reason: "insufficient-wallet");
            Debug.LogWarning($"<color=orange>[DepositToSafe]</color> {character?.CharacterName} insufficient wallet for {_amount} of {_currency} → safe {_safe.FurnitureName} not credited.");
            return;
        }

        _safe.Credit(_currency, _amount, "player-deposit");
        Debug.Log($"<color=green>[DepositToSafe]</color> {character?.CharacterName} deposited {_amount} of {_currency} into {_safe.FurnitureName}.");
    }
}
