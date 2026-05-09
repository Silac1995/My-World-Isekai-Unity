using System;
using System.Collections.Generic;

[Serializable]
public class WalletSaveData
{
    public List<CurrencyBalanceEntry> balances = new List<CurrencyBalanceEntry>();
}

[Serializable]
public class CurrencyBalanceEntry
{
    public int currencyId;
    public int amount;
}
