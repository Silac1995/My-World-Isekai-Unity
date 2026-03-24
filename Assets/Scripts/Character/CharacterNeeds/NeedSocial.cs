using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _lowThreshold = 30f;

    private float _searchCooldown = 15f; 
    private float _lastSearchTime = -999f;

    public NeedSocial(Character character, float startValue = 80f) : base(character)
    {
        _currentValue = startValue;
    }

    // Call this if the action fails to find a partner
    public void SetCooldown()
    {
        _lastSearchTime = UnityEngine.Time.time;
    }

    public void IncreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue + amount, 0, _maxValue);
    public void DecreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue - amount, 0, _maxValue);

    public override float CurrentValue 
    { 
        get => _currentValue; 
        set => _currentValue = Mathf.Clamp(value, 0, _maxValue); 
    }

    public bool IsLow() => _currentValue <= _lowThreshold;

    public override bool IsActive()
    {
        if (UnityEngine.Time.time - _lastSearchTime < _searchCooldown) return false;
        return IsLow() && !_character.CharacterInteraction.IsInteracting;
    }

    public override float GetUrgency()
    {
        return 100f - _currentValue;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("Socialize", new Dictionary<string, bool> { { "isLonely", false } }, (int)GetUrgency());
    }

    public override List<GoapAction> GetGoapActions()
    {
        return new List<GoapAction> 
        {
            new GoapAction_Socialize() 
        };
    }
}
