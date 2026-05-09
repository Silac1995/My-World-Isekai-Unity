using UnityEngine;

/// <summary>
/// Represents a modification applied to a stat.
/// Keeps track of the origin (Source) of the bonus/malus so it can be removed specifically later.
/// </summary>
public class StatModifier
{
    public float Value { get; private set; }
    public object Source { get; private set; }

    public StatModifier(float value, object source)
    {
        Value = value;
        Source = source;
    }
}
