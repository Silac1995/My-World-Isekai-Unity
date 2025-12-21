using UnityEngine;

[System.Serializable]
public abstract class CharacterSecondaryStats : CharacterBaseStats
{
    private float maxValue = 100f;

    protected CharacterSecondaryStats(CharacterStats characterStats, float baseValue = 1)
        : base(characterStats, baseValue)
    {
        currentValue = baseValue;
    }

    public float Value => currentValue;

    public void Modify(float delta)
    {
        currentValue = Mathf.Clamp(currentValue + delta, 0f, maxValue);
    }

}
