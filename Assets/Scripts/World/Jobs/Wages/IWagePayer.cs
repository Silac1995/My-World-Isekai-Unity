using MWI.Economy;

/// <summary>
/// Pays wages to a character. v1 implementation (MintedWagePayer) mints coins from nothing.
/// Future implementations may deduct from a building treasury, fail when insufficient, etc.
/// </summary>
public interface IWagePayer
{
    /// <summary>
    /// Credits the worker's wallet with the wage amount in the given currency.
    /// Implementations are responsible for any validation, treasury accounting, and logging.
    /// Must be safe to call on the server only — caller is responsible for the server-authority gate.
    /// </summary>
    void PayWages(Character worker, CurrencyId currency, int coins, string source);
}
