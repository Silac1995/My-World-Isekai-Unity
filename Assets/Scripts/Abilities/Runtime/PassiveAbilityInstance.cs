using UnityEngine;

[System.Serializable]
public class PassiveAbilityInstance : AbilityInstance
{
    [SerializeField] private float _internalCooldownRemaining;

    public PassiveAbilitySO PassiveData => (PassiveAbilitySO)_data;
    public float InternalCooldownRemaining => _internalCooldownRemaining;
    public bool IsOnCooldown => _internalCooldownRemaining > 0f;

    public PassiveAbilityInstance(PassiveAbilitySO data, Character owner) : base(data, owner)
    {
        _internalCooldownRemaining = 0f;
    }

    public override bool CanUse(Character target)
    {
        // Passives are not manually used — they trigger automatically
        return false;
    }

    /// <summary>
    /// Attempts to trigger this passive. Returns true if it fired.
    /// </summary>
    /// <param name="condition">The combat event that occurred.</param>
    /// <param name="source">The character who caused the event (attacker, etc.).</param>
    /// <param name="target">The character affected by the event (damage receiver, etc.).</param>
    public bool TryTrigger(PassiveTriggerCondition condition, Character source, Character target)
    {
        if (_data == null || _owner == null) return false;
        if (!_owner.IsAlive()) return false;

        var passiveData = PassiveData;

        // Must match the trigger condition
        if (passiveData.TriggerCondition != condition) return false;

        // Must not be on internal cooldown
        if (_internalCooldownRemaining > 0f) return false;

        // HP threshold check for OnLowHPThreshold
        if (condition == PassiveTriggerCondition.OnLowHPThreshold)
        {
            if (_owner.Stats?.Health == null) return false;
            float hpPercent = _owner.Stats.Health.CurrentAmount / _owner.Stats.Health.MaxValue;
            if (hpPercent > passiveData.HpThreshold) return false;
        }

        // Roll trigger chance
        if (passiveData.TriggerChance < 1f && Random.value > passiveData.TriggerChance)
            return false;

        // Trigger succeeded — start internal cooldown
        _internalCooldownRemaining = passiveData.InternalCooldown;
        return true;
    }

    public void TickCooldown(float deltaTime)
    {
        if (_internalCooldownRemaining > 0f)
            _internalCooldownRemaining = Mathf.Max(0f, _internalCooldownRemaining - deltaTime);
    }
}
