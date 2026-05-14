using System.Collections.Generic;
using MWI.Economy;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative shop-purchase action. Single class, two completion
/// gates (NPC: 2s timer; Player: UI Confirm via ServerRpc).
///
/// Lifecycle:
/// 1. CanExecute — cashier free + has vendor (when required) + customer not null.
/// 2. OnStart   — TryAcquireCustomerLock; opens UI on customer client (Player mode).
/// 3. OnTick    — NPC: timer; Player: waits for _hasPlayerSelection.
/// 4. Commit    — atomic: validate funds, pull items from sell-shelves with
///                rollback on partial failure, deliver via PickUpItem cascade,
///                debit wallet, credit till, release lock.
/// 5. OnCancel  — refund/release if not yet committed; close UI on customer client.
///
/// Mirrors CharacterAction_FinishConstruction (2026-05-06 spec) for pattern.
/// </summary>
public class CharacterAction_BuyFromShop : CharacterAction_Continuous
{
    public enum BuyMode { NPC, Player }

    private readonly Cashier _cashier;
    private readonly List<ItemSO> _itemsToBuy;
    private readonly Dictionary<ItemSO, int> _quantities;
    private readonly BuyMode _mode;

    private float _elapsed;
    private bool _hasPlayerSelection;
    private bool _commitDone;

    private const float NPC_DURATION = 2f;
    private const float SENTINEL_TIMEOUT = 600f;

    public Cashier Cashier => _cashier;
    public BuyMode Mode => _mode;

    public CharacterAction_BuyFromShop(Character customer, Cashier cashier, List<ItemSO> itemsToBuy, BuyMode mode)
        : base(customer)
    {
        _cashier = cashier;
        _itemsToBuy = itemsToBuy ?? new List<ItemSO>();
        _quantities = new Dictionary<ItemSO, int>();
        _mode = mode;
        TickIntervalSeconds = 0.2f;

        if (mode == BuyMode.NPC)
        {
            for (int i = 0; i < _itemsToBuy.Count; i++)
            {
                var so = _itemsToBuy[i];
                if (so == null) continue;
                _quantities[so] = (_quantities.TryGetValue(so, out var v) ? v : 0) + 1;
            }
        }
    }

    public override bool CanExecute()
    {
        if (_cashier == null || _cashier.LinkedShop == null) return false;
        if (!_cashier.IsAvailableForCustomer) return false;
        return true;
    }

    public override void OnStart()
    {
        if (!_cashier.TryAcquireCustomerLock(character))
        {
            Debug.LogWarning($"[BuyFromShop] {character?.CharacterName} failed to acquire lock at OnStart — aborting.");
            Finish();
            return;
        }
        if (_mode == BuyMode.Player && _cashier.NetSync != null)
        {
            ulong customerId = character != null ? character.NetworkObjectId : 0UL;
            ulong cashierId = _cashier.NetSync.NetworkObjectId;
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.OpenBuyPanelClientRpc(customerId, cashierId, p);
        }
    }

    public override bool OnTick()
    {
        _elapsed += TickIntervalSeconds;
        if (_elapsed > SENTINEL_TIMEOUT)
        {
            Debug.LogWarning("[BuyFromShop] sentinel timeout — aborting.");
            return true; // OnCancel will run via the Finish path; lock cleanup happens there.
        }

        if (_mode == BuyMode.NPC) return _elapsed >= NPC_DURATION && Commit();
        if (_mode == BuyMode.Player) return _hasPlayerSelection && Commit();
        return false;
    }

    /// <summary>
    /// Server-only — called by CashierNetSync.SubmitPlayerSelectionServerRpc when
    /// the player presses Confirm in the buy UI.
    /// </summary>
    internal void ApplyPlayerSelection(IReadOnlyList<(ItemSO item, int qty)> selections)
    {
        _itemsToBuy.Clear();
        _quantities.Clear();
        if (selections == null) { _hasPlayerSelection = true; return; }
        for (int i = 0; i < selections.Count; i++)
        {
            var (so, qty) = selections[i];
            if (so == null || qty <= 0) continue;
            _itemsToBuy.Add(so);
            _quantities[so] = qty;
        }
        _hasPlayerSelection = true;
    }

    public override void OnCancel()
    {
        if (!_commitDone) ReleaseLockOnly();
        if (_mode == BuyMode.Player && _cashier?.NetSync != null && character != null)
        {
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.CloseBuyPanelClientRpc(character.NetworkObjectId, p);
        }
    }

    private void ReleaseLockOnly()
    {
        if (_cashier != null && character != null) _cashier.ReleaseCustomerLock(character);
    }

    private bool Commit()
    {
        if (_commitDone) return true;
        var shop = _cashier.LinkedShop;
        if (shop == null) { Abort("shop missing"); return true; }

        // 1) Resolve total cost from the authoritative catalog.
        int totalCost = 0;
        foreach (var so in _quantities.Keys)
        {
            var entry = shop.GetCatalogEntry(so);
            if (entry == null) { Abort($"item {so?.ItemName ?? "?"} not in catalog"); return true; }
            totalCost += ShopBuilding.ResolvePrice(entry.Value) * _quantities[so];
        }

        // 2) Affordability gate (final server-side check; player UI also pre-gates).
        if (totalCost > 0 && !character.CharacterWallet.CanAfford(CurrencyId.Default, totalCost))
        {
            Abort("insufficient funds");
            return true;
        }

        // 3) Pull each ItemInstance from the sell-shelves; rollback on partial failure.
        var pulled = new List<(StorageFurniture shelf, ItemInstance instance)>();
        foreach (var pair in _quantities)
        {
            var so = pair.Key;
            int qty = pair.Value;
            for (int i = 0; i < qty; i++)
            {
                if (!TryPullFromAnyShelf(shop.SellShelves, so, out var shelf, out var instance))
                {
                    RollbackPulls(pulled);
                    AbortWithToast($"{so.ItemName} is no longer available — purchase cancelled.");
                    return true;
                }
                pulled.Add((shelf, instance));
            }
        }

        // 4) Deliver each pulled ItemInstance to the customer.
        for (int i = 0; i < pulled.Count; i++)
            DeliverToCustomer(pulled[i].instance);

        // 5) Money: customer wallet → cashier till.
        if (totalCost > 0)
        {
            if (!character.CharacterWallet.RemoveCoins(CurrencyId.Default, totalCost, $"ShopPurchase_{shop.BuildingName}"))
            {
                // Should be impossible after the affordability gate; defensive rollback.
                RollbackPulls(pulled);
                Abort("wallet debit failed");
                return true;
            }
            _cashier.CreditTill(CurrencyId.Default, totalCost, $"PurchaseBy_{character.CharacterName}");
        }

        _cashier.ReleaseCustomerLock(character);
        _commitDone = true;
        return true;
    }

    private static bool TryPullFromAnyShelf(IReadOnlyList<StorageFurniture> shelves, ItemSO target, out StorageFurniture pickedShelf, out ItemInstance pickedInstance)
    {
        pickedShelf = null;
        pickedInstance = null;
        if (shelves == null) return false;

        for (int i = 0; i < shelves.Count; i++)
        {
            var shelf = shelves[i];
            if (shelf == null) continue;
            for (int s = 0; s < shelf.Capacity; s++)
            {
                var slot = shelf.GetItemSlot(s);
                if (slot == null || slot.IsEmpty()) continue;
                if (slot.ItemInstance.ItemSO == target)
                {
                    pickedInstance = slot.ItemInstance;
                    if (!shelf.RemoveItem(pickedInstance)) continue;
                    pickedShelf = shelf;
                    return true;
                }
            }
        }
        return false;
    }

    private static void RollbackPulls(List<(StorageFurniture shelf, ItemInstance instance)> pulled)
    {
        for (int i = 0; i < pulled.Count; i++)
        {
            var (shelf, instance) = pulled[i];
            if (shelf == null || instance == null) continue;
            if (!shelf.AddItem(instance))
                Debug.LogError($"[BuyFromShop] Rollback failed: {instance.ItemSO.ItemName} could not be returned to {shelf.name}. Item lost.");
        }
    }

    private void DeliverToCustomer(ItemInstance instance)
    {
        // Multiplayer authority gate. Bag-inventory contents are NOT replicated by
        // CharacterEquipment._networkEquipment (which only covers weapon / bag-shell /
        // wearable slots). When the customer's Character is owned by a remote client,
        // mutating the server-side _bag.Inventory.ItemSlots is invisible to the owner.
        // Hand the delivery off to the owner via ReceiveItemPickupClientRpc — the
        // owner runs PickUpItem on its own local inventory. Mirrors the same branch
        // in WorldItem.RequestInteractServerRpc. NPCs and the host both have
        // IsOwnedByServer == true so they keep the direct-PickUpItem path.
        if (character.IsSpawned && !character.IsOwnedByServer && character.CharacterActions != null)
        {
            var itemData = new NetworkItemData
            {
                ItemId = new FixedString64Bytes(instance.ItemSO.ItemId),
                JsonData = new FixedString4096Bytes(JsonUtility.ToJson(instance))
            };
            character.CharacterActions.ReceiveItemPickupClientRpc(itemData);
            Debug.Log($"<color=green>[BuyFromShop]</color> Routed delivery of {instance.ItemSO.ItemName} to remote owner (client {character.OwnerClientId}).");
            return;
        }

        if (character.CharacterEquipment.PickUpItem(instance)) return;

        SpawnAsWorldItemNextToCharacter(instance);
        if (_mode == BuyMode.Player && _cashier?.NetSync != null)
        {
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.ToastClientRpc(
                $"{instance.ItemSO.ItemName} dropped on the ground",
                MWI.UI.Notifications.ToastType.Info,
                p);
        }
    }

    private void SpawnAsWorldItemNextToCharacter(ItemInstance instance)
    {
        // Reuse the existing physical-drop helper used by CharacterDropItem / DropItemFromHand.
        CharacterDropItem.ExecutePhysicalDrop(character, instance);
    }

    private void Abort(string reason)
    {
        Debug.Log($"[BuyFromShop] Abort: {reason}");
        _cashier?.ReleaseCustomerLock(character);
        _commitDone = true; // suppress duplicate cleanup in OnCancel
    }

    private void AbortWithToast(string message)
    {
        Debug.Log($"[BuyFromShop] Abort: {message}");
        if (_mode == BuyMode.Player && _cashier?.NetSync != null && character != null)
        {
            ClientRpcParams p = new() { Send = new ClientRpcSendParams { TargetClientIds = new[] { character.OwnerClientId } } };
            _cashier.NetSync.ToastClientRpc(message, MWI.UI.Notifications.ToastType.Warning, p);
        }
        _cashier?.ReleaseCustomerLock(character);
        _commitDone = true;
    }
}
