#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MWI.WorldSystem;

public static class BuildingRegistryToBuildingSOMigration
{
    private const string OutputFolder = "Assets/Resources/Data/Buildings";
    private const string SettingsAssetPath = "Assets/Resources/Data/World/WorldSettingsData.asset";

    [MenuItem("MWI/Migration/Convert BuildingRegistry → BuildingSO assets")]
    public static void Run()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WorldSettingsData>(SettingsAssetPath);
        if (settings == null)
        {
            Debug.LogError($"[Migration] No WorldSettingsData at {SettingsAssetPath}.");
            return;
        }

        // Read both fields via SerializedObject. Post-Task-18 cleanup the legacy
        // `List<BuildingRegistryEntry>` field is gone — the C# `BuildingRegistry`
        // field is now the new `List<BuildingSO>` (renamed from `Blueprints` via
        // [FormerlySerializedAs]). If the legacy `Blueprints` property still
        // resolves, Task 7 layout is in place and we can run the migration.
        // Otherwise we're already on the post-cleanup layout — bail cleanly.
        var so = new SerializedObject(settings);
        var legacyProp = so.FindProperty("BuildingRegistry");
        var blueprintsProp = so.FindProperty("Blueprints");
        if (blueprintsProp == null)
        {
            Debug.Log("[Migration] Post-cleanup layout detected (no `Blueprints` field). Migration already complete — nothing to do.");
            return;
        }
        if (legacyProp == null || !legacyProp.isArray || !blueprintsProp.isArray)
        {
            Debug.LogError("[Migration] Expected both BuildingRegistry (legacy) and Blueprints (new) properties on WorldSettingsData. Has Task 7 landed?");
            return;
        }

        if (blueprintsProp.arraySize > 0)
        {
            Debug.Log("[Migration] Blueprints is already populated. Nothing to do (delete Blueprints entries in Inspector to force re-migration).");
            return;
        }

        if (legacyProp.arraySize == 0)
        {
            Debug.LogWarning("[Migration] Legacy BuildingRegistry is empty — nothing to migrate.");
            return;
        }

        if (!Directory.Exists(OutputFolder)) Directory.CreateDirectory(OutputFolder);

        var createdSOs = new List<BuildingSO>();
        for (int i = 0; i < legacyProp.arraySize; i++)
        {
            var entryProp = legacyProp.GetArrayElementAtIndex(i);
            string prefabId = entryProp.FindPropertyRelative("PrefabId").stringValue;
            string buildingName = entryProp.FindPropertyRelative("BuildingName").stringValue;
            var iconObj = entryProp.FindPropertyRelative("Icon").objectReferenceValue as Sprite;
            var buildingPrefabObj = entryProp.FindPropertyRelative("BuildingPrefab").objectReferenceValue as GameObject;
            var interiorPrefabObj = entryProp.FindPropertyRelative("InteriorPrefab").objectReferenceValue as GameObject;
            int communityPriority = entryProp.FindPropertyRelative("CommunityPriority").intValue;

            if (string.IsNullOrEmpty(prefabId))
            {
                Debug.LogWarning($"[Migration] Registry row {i} has empty PrefabId — skipped.");
                continue;
            }

            // Pull the prefab's existing _buildingType + _constructionRequirements + _defaultFurnitureLayout.
            BuildingType bType = BuildingType.Residential;
            var constructionReqs = new List<CraftingIngredient>();
            var defaultLayout = new List<Building.DefaultFurnitureSlot>();
            bool isCommercial = false;
            if (buildingPrefabObj != null)
            {
                var building = buildingPrefabObj.GetComponent<Building>();
                if (building != null)
                {
                    var bSo = new SerializedObject(building);
                    bType = (BuildingType)bSo.FindProperty("_buildingType").enumValueIndex;

                    var crProp = bSo.FindProperty("_constructionRequirements");
                    if (crProp != null)
                    {
                        for (int j = 0; j < crProp.arraySize; j++)
                        {
                            var item = crProp.GetArrayElementAtIndex(j).FindPropertyRelative("Item").objectReferenceValue as ItemSO;
                            int amount = crProp.GetArrayElementAtIndex(j).FindPropertyRelative("Amount").intValue;
                            if (item != null) constructionReqs.Add(new CraftingIngredient { Item = item, Amount = amount });
                        }
                    }

                    var dflProp = bSo.FindProperty("_defaultFurnitureLayout");
                    if (dflProp != null)
                    {
                        for (int j = 0; j < dflProp.arraySize; j++)
                        {
                            var slotProp = dflProp.GetArrayElementAtIndex(j);
                            defaultLayout.Add(new Building.DefaultFurnitureSlot
                            {
                                ItemSO = slotProp.FindPropertyRelative("ItemSO").objectReferenceValue as FurnitureItemSO,
                                LocalPosition = slotProp.FindPropertyRelative("LocalPosition").vector3Value,
                                LocalEulerAngles = slotProp.FindPropertyRelative("LocalEulerAngles").vector3Value,
                                TargetRoom = slotProp.FindPropertyRelative("TargetRoom").objectReferenceValue as Room
                            });
                        }
                    }
                }

                isCommercial = buildingPrefabObj.GetComponent<CommercialBuilding>() != null;
            }

            BuildingSO blueprint = isCommercial
                ? ScriptableObject.CreateInstance<BuildingCommercialSO>()
                : ScriptableObject.CreateInstance<BuildingSO>();
            blueprint.name = prefabId;

            // Reflection write — fields are private SerializeFields.
            var t = blueprint.GetType();
            void WriteField(string fieldName, object value)
            {
                var f = t.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                     ?? t.BaseType?.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(blueprint, value);
            }
            WriteField("_prefabId", prefabId);
            WriteField("_buildingName", buildingName);
            WriteField("_icon", iconObj);
            WriteField("_buildingPrefab", buildingPrefabObj);
            WriteField("_interiorPrefab", interiorPrefabObj);
            WriteField("_communityPriority", communityPriority);
            WriteField("_buildingType", bType);
            WriteField("_constructionRequirements", constructionReqs);
            WriteField("_defaultFurnitureLayout", defaultLayout);

            string assetPath = $"{OutputFolder}/{prefabId}.asset";
            AssetDatabase.CreateAsset(blueprint, assetPath);
            createdSOs.Add(blueprint);
            Debug.Log($"[Migration] Created {assetPath} (commercial={isCommercial}).");

            // Re-target the prefab's _blueprint field to the new SO.
            if (buildingPrefabObj != null)
            {
                var building = buildingPrefabObj.GetComponent<Building>();
                if (building != null)
                {
                    var bSo = new SerializedObject(building);
                    var bp = bSo.FindProperty("_blueprint");
                    if (bp != null)
                    {
                        bp.objectReferenceValue = blueprint;
                        bSo.ApplyModifiedProperties();
                        PrefabUtility.SavePrefabAsset(buildingPrefabObj);
                        Debug.Log($"[Migration] Set _blueprint on prefab '{buildingPrefabObj.name}'.");
                    }
                    else
                    {
                        Debug.LogWarning($"[Migration] Prefab '{buildingPrefabObj.name}' has no _blueprint field — has Task 5 landed?");
                    }
                }
            }
        }

        // Populate the new Blueprints list. The legacy BuildingRegistry stays
        // intact (Task 18 deletes it after verification).
        blueprintsProp.ClearArray();
        for (int i = 0; i < createdSOs.Count; i++)
        {
            blueprintsProp.InsertArrayElementAtIndex(i);
            blueprintsProp.GetArrayElementAtIndex(i).objectReferenceValue = createdSOs[i];
        }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Migration] Done. {createdSOs.Count} BuildingSO assets created and wired. Blueprints list populated; legacy BuildingRegistry preserved until Task 18 cleanup.");
    }
}
#endif
