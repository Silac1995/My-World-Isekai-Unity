using System;
using System.Collections.Generic;
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
    /// Returns the GOAP action chain to satisfy hunger. Two paths are considered:
    /// <list type="number">
    ///   <item>
    ///     <description><b>World-item food</b> (preempts when present): a loose
    ///     <see cref="WorldItem"/> within <see cref="CharacterAwareness.AwarenessRadius"/>
    ///     whose instance is a <see cref="FoodInstance"/>. Chain:
    ///     [ <see cref="GoapAction_GoToWorldFood"/>,
    ///       <see cref="GoapAction_PickupWorldFood"/>,
    ///       <see cref="GoapAction_EatCarriedFood"/> ].
    ///     This wins because food on the ground next to the NPC is closer (and almost
    ///     always faster) than walking back to the workplace.</description>
    ///   </item>
    ///   <item>
    ///     <description><b>Workplace storage food</b> (fallback): a
    ///     <see cref="FoodInstance"/> in any <see cref="StorageFurniture"/> at the NPC's
    ///     current <see cref="CommercialBuilding"/> workplace. Chain:
    ///     [ <see cref="GoapAction_GoToFood"/>, <see cref="GoapAction_Eat"/> ].</description>
    ///   </item>
    /// </list>
    /// Both paths share the single <see cref="_searchCooldown"/> bucket — there is no
    /// separate cooldown for world-item scans.
    /// </summary>
    public override List<GoapAction> GetGoapActions()
    {
        if (_character == null)
        {
            Debug.LogWarning("<color=orange>[NeedHunger]</color> GetGoapActions: _character is null.");
            return new List<GoapAction>();
        }

        // 1. World-item path (preempts).
        var worldFoodActions = TryFindWorldFood();
        if (worldFoodActions != null)
        {
            _lastSearchTime = UnityEngine.Time.time;
            return worldFoodActions;
        }

        // 2. Workplace storage path (fallback).
        var workplaceFoodActions = TryFindWorkplaceFood();
        if (workplaceFoodActions != null)
        {
            _lastSearchTime = UnityEngine.Time.time;
            return workplaceFoodActions;
        }

        // 3. Nothing found. Start the cooldown to avoid GOAP spam.
        Debug.Log($"<color=cyan>[NeedHunger]</color> {_character.CharacterName}: no food found on the ground or at workplace. Starting cooldown.");
        _lastSearchTime = UnityEngine.Time.time;
        return new List<GoapAction>();
    }

    /// <summary>
    /// Scans the NPC's <see cref="CharacterAwareness"/> radius for a loose
    /// <see cref="WorldItem"/> backed by a <see cref="FoodInstance"/>. Returns the
    /// world-item action chain if one is found, otherwise null. The shared awareness
    /// list is read-only and must not be held across ticks (see
    /// <see cref="CharacterAwareness.GetVisibleInteractables"/>) — we iterate inline.
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
                if (visible[i] is not WorldItem worldItem) continue;
                if (worldItem.IsBeingCarried) continue;
                if (worldItem.ItemInstance is not FoodInstance foodInstance) continue;

                Debug.Log($"<color=green>[NeedHunger]</color> {_character.CharacterName} spotted loose food '{foodInstance.CustomizedName}' on the ground — preempting workplace path.");

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

    /// <summary>
    /// Scans the NPC's job <see cref="CommercialBuilding"/> for a
    /// <see cref="FoodInstance"/> living in a <see cref="StorageFurniture"/> slot.
    /// Returns the workplace action chain if a hit is found, otherwise null.
    /// </summary>
    private List<GoapAction> TryFindWorkplaceFood()
    {
        CommercialBuilding workplace = _character.CharacterJob?.Workplace;
        if (workplace == null)
        {
            Debug.Log($"<color=cyan>[NeedHunger]</color> {_character.CharacterName} has no Workplace — cannot locate workplace food.");
            return null;
        }

        try
        {
            foreach (var (furniture, item) in workplace.GetItemsInStorageFurniture())
            {
                if (item?.ItemSO is FoodSO)
                {
                    Debug.Log($"<color=green>[NeedHunger]</color> {_character.CharacterName} found food '{item.CustomizedName}' in '{furniture.FurnitureName}' at '{workplace.BuildingName}'.");
                    return new List<GoapAction>
                    {
                        new GoapAction_GoToFood(furniture),
                        new GoapAction_Eat(furniture)
                    };
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[NeedHunger]</color> {_character.CharacterName}: exception while scanning '{workplace.BuildingName}' for food.");
        }

        return null;
    }
}
