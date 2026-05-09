using System;
using System.Collections.Generic;
using MWI.Economy;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-character wallet with multi-currency balances.
/// Server-authoritative — mutations must originate on the server (or route through a ServerRpc).
/// Persisted via ICharacterSaveData&lt;WalletSaveData&gt;.
/// Today uses a plain Dictionary synced via ClientRpc on change; sufficient while only
/// a single "Default" currency exists. When Kingdom lands and we have N currencies per
/// character, upgrade the sync path to NetworkList&lt;CurrencyBalanceEntry&gt; without
/// changing callers.
/// </summary>
public class CharacterWallet : CharacterSystem, ICharacterSaveData<WalletSaveData>
{
    private readonly Dictionary<CurrencyId, int> _balances = new Dictionary<CurrencyId, int>();

    public event Action<CurrencyId, int, int> OnBalanceChanged; // currency, oldValue, newValue
    public event Action<CurrencyId, int, string> OnCoinsReceived; // currency, amount, source

    // --- Public read API ---

    public int GetBalance(CurrencyId currency)
    {
        return _balances.TryGetValue(currency, out int v) ? v : 0;
    }

    public IReadOnlyDictionary<CurrencyId, int> GetAllBalances() => _balances;

    public bool CanAfford(CurrencyId currency, int amount)
    {
        if (amount <= 0) return true;
        return GetBalance(currency) >= amount;
    }

    // --- Public mutation API (server-authoritative) ---

    public void AddCoins(CurrencyId currency, int amount, string source)
    {
        if (amount <= 0)
        {
            Debug.LogError($"[CharacterWallet] AddCoins rejected: amount={amount} source={source} on {SafeOwnerName()}");
            return;
        }
        if (!IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.LogError($"[CharacterWallet] AddCoins called on non-server instance for {SafeOwnerName()}. Route through a ServerRpc.");
            return;
        }
        int old = GetBalance(currency);
        int next = old + amount;
        _balances[currency] = next;
        OnBalanceChanged?.Invoke(currency, old, next);
        OnCoinsReceived?.Invoke(currency, amount, source);
        BroadcastBalanceChangeClientRpc(currency.Id, next);
    }

    public bool RemoveCoins(CurrencyId currency, int amount, string reason)
    {
        if (amount <= 0) { Debug.LogError($"[CharacterWallet] RemoveCoins rejected: amount={amount} reason={reason} on {SafeOwnerName()}"); return false; }
        if (!IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.LogError($"[CharacterWallet] RemoveCoins called on non-server instance for {SafeOwnerName()}. Route through a ServerRpc.");
            return false;
        }
        int old = GetBalance(currency);
        if (old < amount) return false;
        int next = old - amount;
        _balances[currency] = next;
        OnBalanceChanged?.Invoke(currency, old, next);
        BroadcastBalanceChangeClientRpc(currency.Id, next);
        return true;
    }

    private string SafeOwnerName()
    {
        if (_character == null) return "<no-character>";
        var name = _character.CharacterName;
        return string.IsNullOrEmpty(name) ? "<unnamed>" : name;
    }

    [ClientRpc]
    private void BroadcastBalanceChangeClientRpc(int currencyRawId, int newValue)
    {
        if (IsServer) return; // server already applied it
        var currency = new CurrencyId(currencyRawId);
        int old = GetBalance(currency);
        _balances[currency] = newValue;
        OnBalanceChanged?.Invoke(currency, old, newValue);
    }

    // --- ICharacterSaveData ---

    public string SaveKey => "CharacterWallet";
    public int LoadPriority => 35;

    public WalletSaveData Serialize()
    {
        var data = new WalletSaveData();
        foreach (var kv in _balances)
        {
            data.balances.Add(new CurrencyBalanceEntry { currencyId = kv.Key.Id, amount = kv.Value });
        }
        return data;
    }

    public void Deserialize(WalletSaveData data)
    {
        _balances.Clear();
        if (data == null || data.balances == null) return;
        foreach (var entry in data.balances)
        {
            _balances[new CurrencyId(entry.currencyId)] = entry.amount;
        }
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
