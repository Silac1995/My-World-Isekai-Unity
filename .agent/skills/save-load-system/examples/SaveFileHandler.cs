using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

public static class SaveFileHandler
{
    private static string SaveDir =>
        Path.Combine(Application.persistentDataPath, "Saves");

    public static string SlotPath(int slot) =>
        Path.Combine(SaveDir, $"slot_{slot}.json");

    // Atomic async write: write .tmp then replace real file
    public static async Task WriteAsync(int slot, GameSaveData data)
    {
        Directory.CreateDirectory(SaveDir);
        string path    = SlotPath(slot);
        string tmpPath = path + ".tmp";
        string json    = JsonConvert.SerializeObject(data, Formatting.Indented);

        // Optional: encrypt or checksum json here before writing

        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    public static async Task<GameSaveData> ReadAsync(int slot)
    {
        string path = SlotPath(slot);
        if (!File.Exists(path)) return null;

        try
        {
            string json = await File.ReadAllTextAsync(path);
            return JsonConvert.DeserializeObject<GameSaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveFileHandler] Failed to read slot {slot}: {e.Message}");
            return null;  // Caller handles null -> new game
        }
    }

    public static Task DeleteAsync(int slot)
    {
        string path = SlotPath(slot);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public static bool SlotExists(int slot) => File.Exists(SlotPath(slot));
}
