/// <summary>
/// Interface for character capabilities that have persistent state to save/load.
/// Each CharacterSystem with state implements this with its own save data type.
/// </summary>
/// <typeparam name="T">The save data struct/class for this capability.</typeparam>
public interface ICharacterSaveData<T>
{
    /// <summary>Serialize this capability's state into a save data object.</summary>
    T Serialize();

    /// <summary>Restore this capability's state from a save data object.</summary>
    void Deserialize(T data);
}
