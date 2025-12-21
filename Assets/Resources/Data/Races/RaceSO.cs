using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = "RaceData", menuName = "Race")]
public class RaceSO : ScriptableObject
{

    [Header("Race Info")]
    public string raceName;
    public float bonusHealth;
    public float bonusSpeed;

    [Header("Visual Prefabs")]
    //public List<RigTypeSO> rigTypes = new List<RigTypeSO>();
    public List<GameObject> character_prefabs = new List<GameObject>();
    public List<CharacterVisualPresetSO> characterVisualPresets = new List<CharacterVisualPresetSO>();
}
