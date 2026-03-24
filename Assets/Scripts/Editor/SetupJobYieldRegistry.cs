using UnityEditor;
using UnityEngine;
using MWI.WorldSystem;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

public static class SetupJobYieldRegistry
{
    [MenuItem("Tools/Setup Job Yield Registry")]
    public static void Run()
    {
        string dir = "Assets/Data/World";
        string path = dir + "/JobYieldRegistry_Default.asset";
        
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        JobYieldRegistry obj = AssetDatabase.LoadAssetAtPath<JobYieldRegistry>(path);
        if (obj == null)
        {
            obj = ScriptableObject.CreateInstance<JobYieldRegistry>();
            AssetDatabase.CreateAsset(obj, path);
            
            var list = new List<JobYieldRecipe>();
            foreach (JobType job in System.Enum.GetValues(typeof(JobType)))
            {
                if (job == JobType.None) continue;
                var recipe = new JobYieldRecipe();
                recipe.Job = job;
                recipe.Outputs.Add(new YieldOutput { ResourceId = "wood", BaseAmountPerDay = 5, SkillMultiplierWeight = 0f });
                list.Add(recipe);
            }

            FieldInfo field = typeof(JobYieldRegistry).GetField("_recipes", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) {
                field.SetValue(obj, list);
            }
            EditorUtility.SetDirty(obj);
        }
        
        var maps = Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
        foreach (var map in maps)
        {
            map.JobYields = obj;
            EditorUtility.SetDirty(map);
        }
        
        AssetDatabase.SaveAssets();
        Debug.Log("JobYieldRegistry Created and Assigned to active MapControllers!");
    }
}
