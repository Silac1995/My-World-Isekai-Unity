using System;
using System.Collections.Generic;
using MWI.Needs;
using UnityEngine;

public class NeedHunger : CharacterNeed
{
    private const float DEFAULT_START = 80f;
    private const float DEFAULT_DECAY_PER_PHASE = 25f;
    private const float DEFAULT_SEARCH_COOLDOWN = 15f;

    private readonly NeedHungerMath _math;
    private float _decayPerPhase = DEFAULT_DECAY_PER_PHASE;
    private float _searchCooldown = DEFAULT_SEARCH_COOLDOWN;
    private float _lastSearchTime = -999f;
    private bool _phaseSubscribed;

    // ── Public surface that delegates to the pure math object ──

    /// <summary>Fires whenever CurrentValue changes (passes the new value). HUD subscribes.</summary>
    public event Action<float> OnValueChanged
    {
        add => _math.OnValueChanged += value;
        remove => _math.OnValueChanged -= value;
    }

    /// <summary>Fires only on transitions of IsStarving (true when value first hits 0; false when it rises above 0).</summary>
    public event Action<bool> OnStarvingChanged
    {
        add => _math.OnStarvingChanged += value;
        remove => _math.OnStarvingChanged -= value;
    }

    public float MaxValue => _math.MaxValue;
    public bool IsStarving => _math.IsStarving;

    public override float CurrentValue
    {
        get => _math.CurrentValue;
        set => _math.CurrentValue = value;
    }

    public NeedHunger(Character character, float startValue = DEFAULT_START) : base(character)
    {
        _math = new NeedHungerMath(startValue);
        TrySubscribeToPhase();
    }

    public void IncreaseValue(float amount) => _math.IncreaseValue(amount);
    public void DecreaseValue(float amount) => _math.DecreaseValue(amount);

    public bool IsLow() => _math.IsLow();

    public void SetCooldown() => _lastSearchTime = UnityEngine.Time.time;

    /// <summary>
    /// Subscribes to TimeManager.OnPhaseChanged. Called by the constructor; also re-callable by
    /// CharacterNeeds.Start if TimeManager wasn't ready at character spawn.
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

    private void HandlePhaseChanged(MWI.Time.DayPhase _)
    {
        try
        {
            _math.DecreaseValue(_decayPerPhase);
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
        return _math.IsLow();
    }

    public override float GetUrgency() => _math.MaxValue - _math.CurrentValue;

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("Eat", new Dictionary<string, bool> { { "isHungry", false } }, (int)GetUrgency());
    }

    /// <summary>
    /// Returns the GOAP action chain to satisfy hunger:
    /// [ GoapAction_GoToFood(furniture), GoapAction_Eat(furniture) ]
    /// v1: only scans the NPC's current job Workplace. Home building fallback is deferred.
    /// </summary>
    public override List<GoapAction> GetGoapActions()
    {
        var actions = new List<GoapAction>();

        if (_character == null)
        {
            Debug.LogWarning("<color=orange>[NeedHunger]</color> GetGoapActions: _character is null.");
            return actions;
        }

        // v1 source: the building the NPC currently works at.
        CommercialBuilding workplace = _character.CharacterJob?.Workplace;
        if (workplace == null)
        {
            Debug.Log($"<color=cyan>[NeedHunger]</color> {_character.CharacterName} has no Workplace — cannot locate food.");
            _lastSearchTime = UnityEngine.Time.time;
            return actions;
        }

        try
        {
            foreach (var (furniture, item) in workplace.GetItemsInStorageFurniture())
            {
                if (item?.ItemSO is FoodSO)
                {
                    actions.Add(new GoapAction_GoToFood(furniture));
                    actions.Add(new GoapAction_Eat(furniture));
                    _lastSearchTime = UnityEngine.Time.time;
                    Debug.Log($"<color=green>[NeedHunger]</color> {_character.CharacterName} found food '{item.CustomizedName}' in '{furniture.FurnitureName}' at '{workplace.BuildingName}'.");
                    return actions;
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"<color=red>[NeedHunger]</color> {_character.CharacterName}: exception while scanning '{workplace.BuildingName}' for food.");
        }

        Debug.Log($"<color=cyan>[NeedHunger]</color> {_character.CharacterName}: no FoodInstance found in '{workplace.BuildingName}'. Starting cooldown.");
        _lastSearchTime = UnityEngine.Time.time;
        return actions;
    }
}
