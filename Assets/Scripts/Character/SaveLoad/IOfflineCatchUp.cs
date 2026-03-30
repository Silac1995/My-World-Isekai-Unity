/// <summary>
/// Interface for character capabilities that need offline catch-up during macro-simulation.
/// Any CharacterSystem implementing this will have its offline delta calculated by MacroSimulator
/// when a map wakes up from hibernation.
/// </summary>
public interface IOfflineCatchUp
{
    /// <summary>
    /// Calculate and apply offline state changes for the given elapsed time.
    /// Called by MacroSimulator during map wake-up catch-up.
    /// </summary>
    /// <param name="elapsedDays">Number of days elapsed since last simulation.</param>
    void CalculateOfflineDelta(float elapsedDays);
}
