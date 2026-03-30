using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public static class SaveFileHandler
{
    private static string WorldSaveDir => Path.Combine(Application.persistentDataPath, "Worlds");
    private static string ProfileSaveDir => Path.Combine(Application.persistentDataPath, "Profiles");

    public static string WorldSlotPath(int slot) => Path.Combine(WorldSaveDir, $"world_{slot}.json");
    public static string ProfilePath(string characterGuid) => Path.Combine(ProfileSaveDir, $"{characterGuid}.json");

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
    public static async Task WriteProfileAsync(string characterGuid, CharacterProfileSaveData data)
    {
        Directory.CreateDirectory(ProfileSaveDir);
        string path = ProfilePath(characterGuid);
        string tmpPath = path + ".tmp";
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);

        await File.WriteAllTextAsync(tmpPath, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmpPath, path);
    }

    public static async Task<CharacterProfileSaveData> ReadProfileAsync(string characterGuid)
    {
        string path = ProfilePath(characterGuid);
        if (!File.Exists(path)) return null;

        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonConvert.DeserializeObject<CharacterProfileSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveFileHandler] Failed to read character profile {characterGuid}: {e.Message}");
            return null;
        }
    }

    // --- PROFILE SCANNING ---
    public static List<CharacterProfileSaveData> GetAllProfiles()
    {
        var profiles = new List<CharacterProfileSaveData>();
        if (!Directory.Exists(ProfileSaveDir)) return profiles;

        foreach (string file in Directory.GetFiles(ProfileSaveDir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var profile = JsonConvert.DeserializeObject<CharacterProfileSaveData>(json);
                if (profile != null) profiles.Add(profile);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveFileHandler] Failed to read profile {file}: {e.Message}");
            }
        }

        return profiles;
    }

    // --- UTILITIES ---
    public static Task DeleteWorldAsync(int slot)
    {
        string path = WorldSlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static Task DeleteProfileAsync(string characterGuid)
    {
        string path = ProfilePath(characterGuid);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static bool WorldSlotExists(int slot) => File.Exists(WorldSlotPath(slot));
    public static bool ProfileExists(string characterGuid) => File.Exists(ProfilePath(characterGuid));
}
