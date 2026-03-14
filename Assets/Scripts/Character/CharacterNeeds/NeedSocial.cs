using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class NeedSocial : CharacterNeed
{
    private float _currentValue;
    private float _maxValue = 100f;
    private float _lowThreshold = 30f;

    public NeedSocial(Character character, float startValue = 80f) : base(character)
    {
        _currentValue = startValue;
    }

    public void IncreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue + amount, 0, _maxValue);
    public void DecreaseValue(float amount) => _currentValue = Mathf.Clamp(_currentValue - amount, 0, _maxValue);

    public bool IsLow() => _currentValue <= _lowThreshold;

    public override bool IsActive()
    {
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
