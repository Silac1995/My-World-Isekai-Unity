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

    public static string WorldPath(string worldGuid) => Path.Combine(WorldSaveDir, $"{worldGuid}.json");
    public static string ProfilePath(string characterGuid) => Path.Combine(ProfileSaveDir, $"{characterGuid}.json");

    // --- WORLD SAVING ---
    public static async Task WriteWorldAsync(string worldGuid, GameSaveData data)
    {
        Directory.CreateDirectory(WorldSaveDir);
        string path = WorldPath(worldGuid);
        string tmpPath = path + ".tmp";
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);

        await File.WriteAllTextAsync(tmpPath, json);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmpPath, path);
    }

    public static async Task<GameSaveData> ReadWorldAsync(string worldGuid)
    {
        string path = WorldPath(worldGuid);
        if (!File.Exists(path)) return null;

        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonConvert.DeserializeObject<GameSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveFileHandler] Failed to read world {worldGuid}: {e.Message}");
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

    // --- WORLD SCANNING ---
    public static List<GameSaveData> GetAllWorlds()
    {
        var worlds = new List<GameSaveData>();
        if (!Directory.Exists(WorldSaveDir)) return worlds;

        foreach (string file in Directory.GetFiles(WorldSaveDir, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var world = JsonConvert.DeserializeObject<GameSaveData>(json);
                if (world != null) worlds.Add(world);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveFileHandler] Failed to read world {file}: {e.Message}");
            }
        }

        return worlds;
    }

    // --- UTILITIES ---
    public static Task DeleteWorldAsync(string worldGuid)
    {
        string path = WorldPath(worldGuid);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static Task DeleteProfileAsync(string characterGuid)
    {
        string path = ProfilePath(characterGuid);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static bool WorldExists(string worldGuid) => File.Exists(WorldPath(worldGuid));
    public static bool ProfileExists(string characterGuid) => File.Exists(ProfilePath(characterGuid));
}
