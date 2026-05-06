using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// JSON round-trip coverage for BuildingSaveData's construction snapshot fields.
/// Both ConstructionProgress and DeliveredMaterials must survive JsonUtility ↔ string
/// so wake-up code can pre-warm the UI before the next ConstructionSiteScanner tick.
///
/// NOTE: BuildingSaveData, BuildingState, and DeliveredMaterialEntryDTO live in the
/// predefined Assembly-CSharp, which Unity does not allow asmdef-defined test
/// assemblies to reference directly (the reference is silently dropped). Tests
/// therefore drive every type via runtime reflection, following the same pattern
/// as Assets/Tests/EditMode/AmbitionQuest/QuestOrderingTests.cs.
/// </summary>
public class BuildingSaveDataConstructionTests
{
    private static Assembly _asm;
    private static Type _buildingSaveDataType;
    private static Type _buildingStateType;
    private static Type _dtoType;

    private static FieldInfo _fBuildingId;
    private static FieldInfo _fPrefabId;
    private static FieldInfo _fState;
    private static FieldInfo _fConstructionProgress;
    private static FieldInfo _fDeliveredMaterials;

    private static FieldInfo _fDtoItemAssetGuid;
    private static FieldInfo _fDtoDelivered;

    private static object _stateUnderConstruction;
    private static object _stateComplete;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        Assert.That(_asm, Is.Not.Null, "Assembly-CSharp must be loaded.");

        _buildingSaveDataType = _asm.GetType("MWI.WorldSystem.BuildingSaveData");
        _buildingStateType    = _asm.GetType("MWI.WorldSystem.BuildingState");
        _dtoType              = _asm.GetType("DeliveredMaterialEntryDTO");

        Assert.That(_buildingSaveDataType, Is.Not.Null, "MWI.WorldSystem.BuildingSaveData not found in Assembly-CSharp.");
        Assert.That(_buildingStateType,    Is.Not.Null, "MWI.WorldSystem.BuildingState not found in Assembly-CSharp.");
        Assert.That(_dtoType,              Is.Not.Null, "DeliveredMaterialEntryDTO not found in Assembly-CSharp.");

        _fBuildingId           = _buildingSaveDataType.GetField("BuildingId");
        _fPrefabId             = _buildingSaveDataType.GetField("PrefabId");
        _fState                = _buildingSaveDataType.GetField("State");
        _fConstructionProgress = _buildingSaveDataType.GetField("ConstructionProgress");
        _fDeliveredMaterials   = _buildingSaveDataType.GetField("DeliveredMaterials");

        Assert.That(_fBuildingId,           Is.Not.Null, "BuildingSaveData.BuildingId not found.");
        Assert.That(_fPrefabId,             Is.Not.Null, "BuildingSaveData.PrefabId not found.");
        Assert.That(_fState,                Is.Not.Null, "BuildingSaveData.State not found.");
        Assert.That(_fConstructionProgress, Is.Not.Null, "BuildingSaveData.ConstructionProgress not found.");
        Assert.That(_fDeliveredMaterials,   Is.Not.Null, "BuildingSaveData.DeliveredMaterials not found.");

        _fDtoItemAssetGuid = _dtoType.GetField("ItemAssetGuid");
        _fDtoDelivered     = _dtoType.GetField("Delivered");
        Assert.That(_fDtoItemAssetGuid, Is.Not.Null, "DeliveredMaterialEntryDTO.ItemAssetGuid not found.");
        Assert.That(_fDtoDelivered,     Is.Not.Null, "DeliveredMaterialEntryDTO.Delivered not found.");

        _stateUnderConstruction = Enum.Parse(_buildingStateType, "UnderConstruction");
        _stateComplete          = Enum.Parse(_buildingStateType, "Complete");
    }

    private object MakeDto(string guid, int delivered)
    {
        var dto = Activator.CreateInstance(_dtoType);
        _fDtoItemAssetGuid.SetValue(dto, guid);
        _fDtoDelivered.SetValue(dto, delivered);
        return dto;
    }

    private IList NewDtoList()
    {
        // Construct List<DeliveredMaterialEntryDTO> via reflection so the runtime
        // type matches BuildingSaveData.DeliveredMaterials's field type exactly
        // (JsonUtility is sensitive to declared field type ↔ assigned value type).
        var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(_dtoType);
        return (IList)Activator.CreateInstance(listType);
    }

    [Test]
    public void RoundTrip_ConstructionProgress_PreservedToJson()
    {
        var dto = Activator.CreateInstance(_buildingSaveDataType);
        _fBuildingId.SetValue(dto, "test-id");
        _fPrefabId.SetValue(dto, "Hut_A");
        _fState.SetValue(dto, _stateUnderConstruction);
        _fConstructionProgress.SetValue(dto, 0.42f);

        var list = NewDtoList();
        list.Add(MakeDto("abc123", 50));
        list.Add(MakeDto("def456", 25));
        _fDeliveredMaterials.SetValue(dto, list);

        string json = JsonUtility.ToJson(dto);
        var parsed = JsonUtility.FromJson(json, _buildingSaveDataType);

        Assert.AreEqual("test-id", _fBuildingId.GetValue(parsed));
        Assert.AreEqual("Hut_A",   _fPrefabId.GetValue(parsed));
        Assert.AreEqual(_stateUnderConstruction, _fState.GetValue(parsed));
        Assert.AreEqual(0.42f, (float)_fConstructionProgress.GetValue(parsed), 0.0001f);

        var parsedList = (IList)_fDeliveredMaterials.GetValue(parsed);
        Assert.IsNotNull(parsedList);
        Assert.AreEqual(2, parsedList.Count);

        Assert.AreEqual("abc123", _fDtoItemAssetGuid.GetValue(parsedList[0]));
        Assert.AreEqual(50,       _fDtoDelivered.GetValue(parsedList[0]));
        Assert.AreEqual("def456", _fDtoItemAssetGuid.GetValue(parsedList[1]));
        Assert.AreEqual(25,       _fDtoDelivered.GetValue(parsedList[1]));
    }

    [Test]
    public void RoundTrip_EmptyDeliveredMaterials_DoesNotThrow()
    {
        var dto = Activator.CreateInstance(_buildingSaveDataType);
        _fBuildingId.SetValue(dto, "empty-id");
        _fPrefabId.SetValue(dto, "Hut_B");
        _fState.SetValue(dto, _stateComplete);
        _fConstructionProgress.SetValue(dto, 1f);
        _fDeliveredMaterials.SetValue(dto, NewDtoList());

        Assert.DoesNotThrow(() =>
        {
            string json = JsonUtility.ToJson(dto);
            var parsed = JsonUtility.FromJson(json, _buildingSaveDataType);

            var parsedList = (IList)_fDeliveredMaterials.GetValue(parsed);
            Assert.IsNotNull(parsedList);
            Assert.AreEqual(0, parsedList.Count);
            Assert.AreEqual(1f, (float)_fConstructionProgress.GetValue(parsed), 0.0001f);
            Assert.AreEqual(_stateComplete, _fState.GetValue(parsed));
        });
    }
}
