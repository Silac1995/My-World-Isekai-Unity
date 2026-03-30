/// <summary>
/// Non-generic base interface for discovery by CharacterDataCoordinator.
/// CharacterSystems implement ICharacterSaveData<T> (generic) instead of this directly.
/// </summary>
public interface ICharacterSaveData
{
    string SaveKey { get; }
    int LoadPriority { get; }
    string SerializeToJson();
    void DeserializeFromJson(string json);
}

/// <summary>
/// Typed save data contract for character subsystems.
/// Each subsystem defines its own DTO type T and implements Serialize/Deserialize.
/// The non-generic methods are bridged via CharacterSaveDataHelper.
/// </summary>
public interface ICharacterSaveData<T> : ICharacterSaveData
{
    T Serialize();
    void Deserialize(T data);
}
