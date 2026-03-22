using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public static class SaveFileHandler
{
    private static string WorldSaveDir => Path.Combine(Application.persistentDataPath, "Worlds");
    private static string ProfileSaveDir => Path.Combine(Application.persistentDataPath, "Profiles");

    public static string WorldSlotPath(int slot) => Path.Combine(WorldSaveDir, $"world_{slot}.json");
    public static string ProfilePath(string profileId) => Path.Combine(ProfileSaveDir, $"profile_{profileId}.json");

    // --- WORLD SAVING ---
    public static async Task WriteWorldAsync(int slot, GameSaveData data)
    {
        Directory.CreateDirectory(WorldSaveDir);
        string path = WorldSlotPath(slot);
        string tmpPath = path + ".tmp";
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);

        await File.WriteAllTextAsync(tmpPath, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmpPath, path);
    }

    public static async Task<GameSaveData> ReadWorldAsync(int slot)
    {
        string path = WorldSlotPath(slot);
        if (!File.Exists(path)) return null;

        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonConvert.DeserializeObject<GameSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveFileHandler] Failed to read world slot {slot}: {e.Message}");
            return null;
        }
    }

    // --- PROFILE SAVING ---
    public static async Task WriteProfileAsync(string profileId, CharacterProfileSaveData data)
    {
        Directory.CreateDirectory(ProfileSaveDir);
        string path = ProfilePath(profileId);
        string tmpPath = path + ".tmp";
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);

        await File.WriteAllTextAsync(tmpPath, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmpPath, path);
    }

    public static async Task<CharacterProfileSaveData> ReadProfileAsync(string profileId)
    {
        string path = ProfilePath(profileId);
        if (!File.Exists(path)) return null;

        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonConvert.DeserializeObject<CharacterProfileSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveFileHandler] Failed to read character profile {profileId}: {e.Message}");
            return null;
        }
    }

    // --- UTILITIES ---
    public static Task DeleteWorldAsync(int slot)
    {
        string path = WorldSlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
    
    public static Task DeleteProfileAsync(string profileId)
    {
        string path = ProfilePath(profileId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static bool WorldSlotExists(int slot) => File.Exists(WorldSlotPath(slot));
    public static bool ProfileExists(string profileId) => File.Exists(ProfilePath(profileId));
}
