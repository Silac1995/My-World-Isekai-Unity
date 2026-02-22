using UnityEngine;

[CreateAssetMenu(menuName = "Status Effects/Periodic Stat Effect")]
public class PeriodicStatEffect : StatusEffect
{
    [SerializeField] private StatType _targetStat = StatType.Health;
    [SerializeField] private float _valuePerSecond = 5f;
    [SerializeField] private bool _isPercentage = false;

    public StatType TargetStat => _targetStat;
    public float ValuePerSecond => _valuePerSecond;
    public bool IsPercentage => _isPercentage;
}
