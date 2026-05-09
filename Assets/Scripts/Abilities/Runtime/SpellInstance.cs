using UnityEngine;

[System.Serializable]
public class SpellInstance : AbilityInstance
{
    [SerializeField] private float _remainingCooldown;

    public SpellSO SpellData => (SpellSO)_data;
    public float RemainingCooldown => _remainingCooldown;
    public bool IsOnCooldown => _remainingCooldown > 0f;

    public SpellInstance(SpellSO data, Character owner) : base(data, owner)
    {
        _remainingCooldown = 0f;
    }

    public override bool CanUse(Character target)
    {
        if (_data == null || _owner == null) return false;
        if (!_owner.IsAlive()) return false;

        var spellData = SpellData;

        // Check mana
        if (_owner.Stats?.Mana == null) return false;
        if (_owner.Stats.Mana.CurrentAmount < spellData.ManaCost) return false;

        // Check cooldown
        if (_remainingCooldown > 0f) return false;

        return true;
    }

    /// <summary>
    /// Computes the actual cast time factoring in the owner's SpellCasting (Dexterity-derived).
    /// Returns 0 if instant cast.
    /// </summary>
    public float ComputeCastTime()
    {
        float castingSpeed = _owner?.Stats?.SpellCasting?.CurrentValue ?? 0f;
        return SpellData.ComputeCastTime(castingSpeed);
    }

    public void StartCooldown()
    {
        _remainingCooldown = SpellData.Cooldown;
    }

    public void TickCooldown(float deltaTime)
    {
        if (_remainingCooldown > 0f)
            _remainingCooldown = Mathf.Max(0f, _remainingCooldown - deltaTime);
    }
}
