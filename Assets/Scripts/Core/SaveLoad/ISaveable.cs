// Assets/Scripts/Core/SaveLoad/ISaveable.cs
public interface ISaveable
{
    /// <summary>
    /// Unique key to identify this component in the save dictionary.
    /// Usually something like "CharacterStats" or "WorldTime".
    /// </summary>
    string SaveKey { get; }

    /// <summary>
    /// Captures the serializable DTO (Data Transfer Object) state of this component.
    /// </summary>
    /// <returns>A serializable object (e.g., a simple C# struct/class).</returns>
    object CaptureState();

    /// <summary>
    /// Restores the state from the generic object.
    /// </summary>
    /// <param name="state">The object that was deserialized from JSON.</param>
    void RestoreState(object state);
}
