using UnityEngine;

/// <summary>
/// Random-number seam used by CharacterTameAction's server roll.
/// Keeps UnityEngine.Random out of business logic so the roll is swappable for
/// deterministic tests or modded providers. The default UnityRandomProvider is
/// used in production.
/// </summary>
public interface IRandomProvider
{
    /// <summary>Returns a uniformly-distributed float in [0, 1).</summary>
    float Value();
}

public sealed class UnityRandomProvider : IRandomProvider
{
    public float Value() => Random.value;
}
