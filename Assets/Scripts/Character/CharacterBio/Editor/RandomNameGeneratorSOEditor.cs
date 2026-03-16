#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(RandomNameGeneratorSO))]
public class RandomNameGeneratorSOEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        RandomNameGeneratorSO generator = (RandomNameGeneratorSO)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Bulk Import Operations", EditorStyles.boldLabel);

        if (GUILayout.Button("Import Male Names from TXT"))
        {
            ImportFromFile(generator, GenderType.Male);
        }

        if (GUILayout.Button("Import Female Names from TXT"))
        {
            ImportFromFile(generator, GenderType.Female);
        }

        if (GUILayout.Button("Import Neutral Names from TXT"))
        {
            ImportFromFile(generator, (GenderType)(-1)); // Using -1 or custom enum as neutral flag
        }
    }

    private void ImportFromFile(RandomNameGeneratorSO generator, GenderType targetGender)
    {
        string path = EditorUtility.OpenFilePanel("Select Name List (txt)", "", "txt");
        if (string.IsNullOrEmpty(path)) return;

        string[] lines = File.ReadAllLines(path);
        
        // Nettoyer les lignes (enlever les espaces vides et les doublons)
        List<string> cleanNames = lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Distinct()
            .ToList();

        if (cleanNames.Count == 0)
        {
            Debug.LogWarning("The selected file was empty or contained no valid names.");
            return;
        }

        Undo.RecordObject(generator, "Import Names");

        SerializedObject so = new SerializedObject(generator);
        SerializedProperty prop;

        if (targetGender == GenderType.Male) prop = so.FindProperty("_maleNames");
        else if (targetGender == GenderType.Female) prop = so.FindProperty("_femaleNames");
        else prop = so.FindProperty("_neutralNames"); // Assume Neutral

        // Add to existing, avoid duplicates
        List<string> existingNames = new List<string>();
        for (int i = 0; i < prop.arraySize; i++)
        {
            existingNames.Add(prop.GetArrayElementAtIndex(i).stringValue);
        }

        foreach (string newName in cleanNames)
        {
            if (!existingNames.Contains(newName))
            {
                prop.arraySize++;
                prop.GetArrayElementAtIndex(prop.arraySize - 1).stringValue = newName;
                existingNames.Add(newName);
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(generator);
        
        Debug.Log($"Successfully imported {cleanNames.Count} names.");
    }
}
#endif
