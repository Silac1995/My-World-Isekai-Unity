using System;
using System.Collections.Generic;
using MWI.Economy;
using MWI.Needs;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative hunger need. The actual current value lives in
/// <c>CharacterNeeds._networkedHunger</c> (a <see cref="NetworkVariable{T}"/> of type float
/// with server-write / everyone-read permissions). NeedHunger is a thin wrapper that:
/// <list type="bullet">
/// <item>Reads the network value through <c>CharacterNeeds.NetworkedHungerValue</c>.</item>
/// <item>Routes writes through the server (direct NV write if server, else ServerRpc).</item>
/// <item>Bridges <c>NetworkVariable.OnValueChanged</c> to its existing public events
///       (<see cref="OnValueChanged"/>, <see cref="OnStarvingChanged"/>) so HUD code in
///       <c>UI_HungerBar</c> works unchanged on every peer.</item>
/// </list>
/// </summary>
public class NeedHunger : CharacterNeed
{
    /// <summary>Starting hunger value for a brand-new character (before save-restore).</summary>
    public const float DEFAULT_START = 80f;
    private const float DEFAULT_DECAY_PER_PHASE = 25f;
    private const float DEFAULT_SEARCH_COOLDOWN = 15f;

    /// <summary>Reference back to the owning networked component.
    /// All value reads/writes go through this so NeedHunger can stay a POCO while still
    /// participating in NGO replication.</summary>
    private readonly CharacterNeeds _owner;

    private float _decayPerPhase = DEFAULT_DECAY_PER_PHASE;
    private float _searchCooldown = DEFAULT_SEARCH_COOLDOWN;
    private float _lastSearchTime = -999f;
    private bool _phaseSubscribed;
    private bool _bridgeBound;

    // Cached starving flag, recomputed on every NV change so OnStarvingChanged
    // fires only on transitions (matches the legacy POCO semantics).
    private bool _isStarving;

    /// <summary>Fires whenever CurrentValue changes (passes the new value). HUD subscribes.</summary>
    public event Action<float> OnValueChanged;

    /// <summary>Fires only on transitions of IsStarving (true when value first hits 0; false when it rises above 0).</summary>
    public event Action<bool> OnStarvingChanged;

    /// <summary>Maximum hunger — constant 100, not networked (saves a NV slot).</summary>
    public float MaxValue => NeedHungerMath.DEFAULT_MAX;

    /// <summary>True when the networked value is at or below 0.</summary>
    public bool IsStarving => _isStarving;

    /// <summary>
    /// Reads the current networked value. Setter is server-authoritative —
    /// on a non-server peer it routes through a ServerRpc so the server is always the writer.
    /// </summary>
    public override float CurrentValue
    {
        get
        {
            if (_owner == null) return 0f;
            return _owner.NetworkedHungerValue;
        }
        set
        {
            if (_owner == null) return;

            // Compute the delta we want applied; the server will clamp on the actual write.
            // We DO NOT clamp client-side — the server is always the source of truth.
            // Path A (server): write the absolute value directly.
            // Path B (client): translate to a delta and request via ServerRpc.
            float current = _owner.NetworkedHungerValue;
            float clampedTarget = Mathf.Clamp(value, 0f, MaxValue);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                _owner.ServerSetHunger(clampedTarget);
            }
            else
            {
                float delta = clampedTarget - current;
                if (Mathf.Approximately(delta, 0f)) return;
                _owner.RequestAdjustHungerRpc(delta);
            }
        }
    }

    /// <summary>
    /// New ctor that takes a back-reference to the owning <see cref="CharacterNeeds"/>.
    /// Required so reads/writes can reach the <see cref="NetworkVariable{T}"/>.
    /// </summary>
    public NeedHunger(Character character, CharacterNeeds owner) : base(character)
    {
        _owner = owner;
    }

    /// <summary>
    /// Increases the hunger value (e.g., from eating food). Server: direct write. Client: ServerRpc.
    /// </summary>
    public void IncreaseValue(float amount)
    {
        if (_owner == null) return;
        if (Mathf.Approximately(amount, 0f)) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            _owner.ServerSetHunger(_owner.NetworkedHungerValue + amount);
        }
        else
        {
            _owner.RequestAdjustHungerRpc(amount);
        }
    }

    /// <summary>
    /// Decreases the hunger value (e.g., phase-tick decay). Should usually only be called on the server;
    /// client calls fall through to a ServerRpc as a defensive fallback.
    /// </summary>
    public void DecreaseValue(float amount)
    {
        if (_owner == null) return;
        if (Mathf.Approximately(amount, 0f)) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            _owner.ServerSetHunger(_owner.NetworkedHungerValue - amount);
        }
        else
        {
            _owner.RequestAdjustHungerRpc(-amount);
        }
    }

    public bool IsLow() => CurrentValue <= NeedHungerMath.DEFAULT_LOW_THRESHOLD;

    public void SetCooldown() => _lastSearchTime = UnityEngine.Time.time;

    /// <summary>
    /// Subscribes to TimeManager.OnPhaseChanged. Idempotent. Re-callable from CharacterNeeds.OnNetworkSpawn
    /// in case TimeManager wasn't ready at character spawn.
    /// </summary>
    public void TrySubscribeToPhase()
    {
        if (_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance == null) return;
        MWI.Time.TimeManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        _phaseSubscribed = true;
    }

    public void UnsubscribeFromPhase()
    {
        if (!_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        _phaseSubscribed = false;
    }

    /// <summary>
    /// Wires the NetworkVariable.OnValueChanged → public OnValueChanged/OnStarvingChanged bridge.
    /// Called from CharacterNeeds.OnNetworkSpawn (every peer). Idempotent.
    /// </summary>
    public void BindNetworkBridge()
    {
        if (_bridgeBound || _owner == null) return;
        _owner.SubscribeNetworkedHungerChanged(HandleNetworkedHungerChanged);
        _bridgeBound = true;

        // Apply current value immediately so HUD that's already initialized picks it up.
        // Also seeds _isStarving so the first transition event fires correctly.
        float current = _owner.NetworkedHungerValue;
        _isStarving = current <= 0f;

        // Fire initial OnValueChanged so the HUD that was initialized BEFORE the NV value
        // arrived (e.g., late-joiner, or local owner that initialized HUD in OnNetworkSpawn
        // before the server's value replicated) updates from the default 100 to the real value.
        try { OnValueChanged?.Invoke(current); }
        catch (Exception e) { Debug.LogException(e); }

        if (_isStarving)
        {
            try { OnStarvingChanged?.Invoke(true); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    public void UnbindNetworkBridge()
    {
        if (!_bridgeBound || _owner == null) return;
        _owner.UnsubscribeNetworkedHungerChanged(HandleNetworkedHungerChanged);
        _bridgeBound = false;
    }

    private void HandleNetworkedHungerChanged(float previous, float current)
    {
        try { OnValueChanged?.Invoke(current); }
        catch (Exception e) { Debug.LogException(e); }

        bool nowStarving = current <= 0f;
        if (nowStarving != _isStarving)
        {
            _isStarving = nowStarving;
            try { OnStarvingChanged?.Invoke(_isStarving); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    private void HandlePhaseChanged(MWI.Time.DayPhase _)
    {
        // Server-only: phase decay is authoritative on the server. Clients receive the
        // resulting value via NetworkVariable replication. Without this guard, every peer
        // would decay independently and clobber the network value with conflicting writes
        // (technically writes from non-server are blocked by NV permissions, but the
        // intent is clear: only the server runs decay logic).
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        try
        {
            DecreaseValue(_decayPerPhase);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // ─────────────────────────────── GOAP / IsActive ───────────────────────────────

    public override bool IsActive()
    {
        if (_character == null) return false;
        if (_character.Controller is PlayerController) return false;
        if (UnityEngine.Time.time - _lastSearchTime < _searchCooldown) return false;
        return IsLow();
    }

    public override float GetUrgency() => MaxValue - CurrentValue;

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("Eat", new Dictionary<string, bool> { { "isHungry", false } }, (int)GetUrgency());
    }

    /// <summary>
    /// Returns the GOAP action chain to satisfy hunger. The decision is shop-first with
    /// a single emergency fallback to ground pickup:
    /// <list type="number">
    ///   <item>
    ///     <description><b>Shop food</b> (preferred): scan every <see cref="ShopBuilding"/>
    ///     known to <see cref="BuildingManager"/>, pick the best
    ///     (<see cref="FoodSO"/>, <see cref="ShopBuilding"/>, <see cref="Cashier"/>) triple
    ///     by <see cref="FoodSO.HungerRestored"/>-per-coin (most filling per coin),
    ///     filtered by wallet affordability, sell-shelf stock, available cashier and
    ///     inventory/hands capacity. Chain:
    ///     [ <see cref="GoapAction_BuyFood"/>, <see cref="GoapAction_EatCarriedFood"/> ].
    ///     This is the everyday hunger response — NPCs are expected to buy their meals.</description>
    ///   </item>
    ///   <item>
    ///     <description><b>Ground pickup</b> (emergency only): a loose
    ///     <see cref="WorldItem"/> within <see cref="CharacterAwareness.AwarenessRadius"/>
    ///     whose instance is a <see cref="FoodInstance"/>. Only considered when
    ///     <see cref="CurrentValue"/> is at or below
    ///     <see cref="NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD"/> — i.e., the need is
    ///     at ≥ 90% (NPC is on the brink of starving). Chain:
    ///     [ <see cref="GoapAction_GoToWorldFood"/>,
    ///       <see cref="GoapAction_PickupWorldFood"/>,
    ///       <see cref="GoapAction_EatCarriedFood"/> ].</description>
    ///   </item>
    /// </list>
    /// The legacy workplace-storage path (<see cref="GoapAction_GoToFood"/> +
    /// <see cref="GoapAction_Eat"/>) is intentionally not registered here — NPCs do not
    /// own their employer's storage, and self-serving from it is being held back until
    /// a proper personal/owned-storage concept exists.
    /// Both surviving paths share the single <see cref="_searchCooldown"/> bucket.
    /// </summary>
    public override List<GoapAction> GetGoapActions()
    {
        if (_character == null)
        {
            Debug.LogWarning("<color=orange>[NeedHunger]</color> GetGoapActions: _character is null.");
            return new List<GoapAction>();
        }

        // 1. Shop path — the default route.
        var shopFoodActions = TryFindShopFood();
        if (shopFoodActions != null)
        {
            _lastSearchTime = UnityEngine.Time.time;
            return shopFoodActions;
        }

        // 2. Emergency ground-pickup fallback — only when hunger is at ≥ 90% of need.
        bool isEmergency = CurrentValue <= NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD;
        if (isEmergency)
        {
            var worldFoodActions = TryFindWorldFood();
            if (worldFoodActions != null)
            {
                _lastSearchTime = UnityEngine.Time.time;
                return worldFoodActions;
            }
        }

        // 3. Nothing actionable. Cooldown to avoid GOAP spam.
        string reason = isEmergency
            ? "no affordable shop food AND no loose food in awareness"
            : $"no affordable shop food (hunger {CurrentValue:F0} > emergency threshold {NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD:F0}, ground pickup gated)";
        Debug.Log($"<color=cyan>[NeedHunger]</color> {_character.CharacterName}: {reason}. Starting cooldown.");
        _lastSearchTime = UnityEngine.Time.time;
        return new List<GoapAction>();
    }

    /// <summary>
    /// Scans every <see cref="ShopBuilding"/> registered with <see cref="BuildingManager"/>
    /// for a <see cref="FoodSO"/> catalog entry that the worker can actually buy
    /// (cashier available, wallet covers price, sell-shelf stocked, inventory/hands free)
    /// and picks the <see cref="FoodSO.HungerRestored"/>-per-coin maximum. Returns the
    /// shop action chain if a viable candidate is found, otherwise null.
    /// <para>
    /// Called from <see cref="GetGoapActions"/>, which itself is throttled by
    /// <see cref="_searchCooldown"/> via <see cref="IsActive"/> — so the cross-building
    /// scan only fires when the NPC is genuinely hungry and the cooldown has elapsed.
    /// </para>
    /// </summary>
    private List<GoapAction> TryFindShopFood()
    {
        var bm = BuildingManager.Instance;
        if (bm == null || bm.allBuildings == null) return null;

        var wallet = _character.CharacterWallet;
        var equipment = _character.CharacterEquipment;
        var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;

        FoodSO bestFood = null;
        ShopBuilding bestShop = null;
        Cashier bestCashier = null;
        float bestScore = float.NegativeInfinity;
        int bestPrice = 0;

        try
        {
            for (int b = 0; b < bm.allBuildings.Count; b++)
            {
                if (bm.allBuildings[b] is not ShopBuilding shop) continue;
                if (shop.Catalog == null || shop.Catalog.Count == 0) continue;
                if (shop.SellShelves == null || shop.SellShelves.Count == 0) continue;

                var cashier = shop.GetFirstAvailableCashier();
                if (cashier == null) continue;

                for (int c = 0; c < shop.Catalog.Count; c++)
                {
                    var entry = shop.Catalog[c];
                    if (entry.Item is not FoodSO foodSO) continue;

                    int price = ShopBuilding.ResolvePrice(entry);
                    if (price > 0 && (wallet == null || !wallet.CanAfford(CurrencyId.Default, price))) continue;

                    bool hasBagSpace = equipment != null && equipment.HasFreeSpaceForItemSO(foodSO);
                    bool handsFree = hands != null && hands.AreHandsFree();
                    if (!hasBagSpace && !handsFree) continue;

                    if (!ShopHasItemInStock(shop, foodSO)) continue;

                    // Score: hunger-per-coin. Free items (price ≤ 0) get a very high
                    // score so they always beat priced alternatives (treat as effectively
                    // infinite efficiency without using float.PositiveInfinity, which makes
                    // ties hard to compare). Use HungerRestored * 1000 for the free case.
                    float score = (price <= 0) ? foodSO.HungerRestored * 1000f
                                               : foodSO.HungerRestored / (float)price;
                    if (score <= bestScore) continue;

                    bestScore = score;
                    bestFood = foodSO;
                    bestShop = shop;
                    bestCashier = cashier;
                    bestPrice = price;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[NeedHunger]</color> {_character.CharacterName}: exception while scanning shops for food.");
            return null;
        }

        if (bestFood == null || bestShop == null || bestCashier == null) return null;

        Debug.Log($"<color=green>[NeedHunger]</color> {_character.CharacterName} chose '{bestFood.name}' at '{bestShop.BuildingName}' (price {bestPrice}, hunger {bestFood.HungerRestored}, score {bestScore:F2}).");

        return new List<GoapAction>
        {
            new GoapAction_BuyFood(bestShop, bestCashier, bestFood),
            new GoapAction_EatCarriedFood()
        };
    }

    /// <summary>
    /// Returns true if any sell-shelf slot on <paramref name="shop"/> currently holds an
    /// <see cref="ItemInstance"/> whose <see cref="ItemSO"/> matches
    /// <paramref name="item"/>. Inlined (no LINQ) per rule #34.
    /// </summary>
    private static bool ShopHasItemInStock(ShopBuilding shop, ItemSO item)
    {
        if (shop == null || item == null) return false;
        var shelves = shop.SellShelves;
        if (shelves == null) return false;
        for (int i = 0; i < shelves.Count; i++)
        {
            var shelf = shelves[i];
            if (shelf == null) continue;
            for (int sl = 0; sl < shelf.Capacity; sl++)
            {
                var slot = shelf.GetItemSlot(sl);
                if (slot == null || slot.IsEmpty()) continue;
                if (slot.ItemInstance != null && slot.ItemInstance.ItemSO == item) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Scans the NPC's <see cref="CharacterAwareness"/> radius for a loose
    /// <see cref="WorldItem"/> backed by a <see cref="FoodInstance"/>. Returns the
    /// world-item action chain if one is found, otherwise null. The shared awareness
    /// list is read-only and must not be held across ticks (see
    /// <see cref="CharacterAwareness.GetVisibleInteractables"/>) — we iterate inline.
    /// <para>
    /// Emergency-only. <see cref="GetGoapActions"/> only calls this when
    /// <see cref="CurrentValue"/> ≤ <see cref="NeedHungerMath.DEFAULT_EMERGENCY_THRESHOLD"/>.
    /// </para>
    /// </summary>
    private List<GoapAction> TryFindWorldFood()
    {
        var awareness = _character.CharacterAwareness;
        if (awareness == null) return null;

        try
        {
            var visible = awareness.GetVisibleInteractables();
            if (visible == null) return null;

            for (int i = 0; i < visible.Count; i++)
            {
                var interactable = visible[i];
                if (interactable == null) continue;
                // WorldItem is a sibling NetworkBehaviour on the same GameObject as the
                // InteractableObject (not a subtype), so reach it via GetComponent.
                var worldItem = interactable.GetComponent<WorldItem>();
                if (worldItem == null) continue;
                if (worldItem.IsBeingCarried) continue;
                if (worldItem.ItemInstance is not FoodInstance foodInstance) continue;

                Debug.Log($"<color=orange>[NeedHunger]</color> {_character.CharacterName} is starving (hunger {CurrentValue:F0}) — falling back to ground pickup of '{foodInstance.CustomizedName}'.");

                return new List<GoapAction>
                {
                    new GoapAction_GoToWorldFood(worldItem),
                    new GoapAction_PickupWorldFood(worldItem),
                    new GoapAction_EatCarriedFood()
                };
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[NeedHunger]</color> {_character.CharacterName}: exception while scanning awareness for loose food.");
        }

        return null;
    }
}
