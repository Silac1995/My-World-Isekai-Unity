using MWI.Economy;
using UnityEngine;

/// <summary>
/// v1 wage payer. Mints coins from nothing — no building treasury, no source-of-funds tracking.
/// Future replacement: BuildingTreasuryWagePayer will deduct from CommercialBuilding.Treasury and
/// fail (or log) when insufficient.
/// </summary>
public class MintedWagePayer : IWagePayer
{
    public void PayWages(Character worker, CurrencyId currency, int coins, string source)
    {
        if (worker == null) { Debug.LogError("[MintedWagePayer] PayWages received null worker."); return; }
        if (coins <= 0) return; // zero/negative wage = no-op (e.g., zero attendance)
        var wallet = worker.CharacterWallet;
        if (wallet == null) { Debug.LogError($"[MintedWagePayer] PayWages: worker {worker.CharacterName} has no CharacterWallet — wage of {coins} discarded (source={source})."); return; }
        wallet.AddCoins(currency, coins, source);
    }
}
