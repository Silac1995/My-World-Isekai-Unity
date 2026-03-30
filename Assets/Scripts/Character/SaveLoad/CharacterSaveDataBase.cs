using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Static helper for ICharacterSaveData<T> JSON bridge methods.
/// Subsystems call these in their explicit ICharacterSaveData implementations.
/// </summary>
public static class CharacterSaveDataHelper
{
    public static string SerializeToJson<T>(ICharacterSaveData<T> saveable)
    {
        return JsonConvert.SerializeObject(saveable.Serialize());
    }

    public static void DeserializeFromJson<T>(ICharacterSaveData<T> saveable, string json)
    {
        var data = JsonConvert.DeserializeObject<T>(json);
        if (data != null)
            saveable.Deserialize(data);
        else
            Debug.LogWarning($"[CharacterSaveDataHelper] Deserialization returned null for {saveable.SaveKey}. JSON may be malformed.");
    }
}
