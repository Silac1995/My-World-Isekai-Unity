using UnityEngine;

[System.Serializable]
public abstract class CharacterTertiaryStats : CharacterBaseStats
{
    [SerializeField] private float minValue = 1f;

    protected CharacterTertiaryStats(CharacterStats characterStats, float baseValue = 1f)
        : base(characterStats, baseValue)
    {
        currentValue = Mathf.Max(baseValue, minValue);
    }

    public float Value => currentValue;

    public void Modify(float delta)
    {
        currentValue = Mathf.Max(currentValue + delta, minValue);
    }
}
