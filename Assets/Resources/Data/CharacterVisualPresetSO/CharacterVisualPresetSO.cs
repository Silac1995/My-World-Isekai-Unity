using UnityEngine;

[CreateAssetMenu(fileName = "CharacterVisualPresetSO", menuName = "Scriptable Objects/CharacterVisualPresetSO")]
public class CharacterVisualPresetSO : ScriptableObject
{
    [SerializeField] public GameObject characterDefaultPrefab;
    [SerializeField] public GameObject characterSpritePrefab;

    public bool HasVisualPrefab()
    {
        return characterSpritePrefab != null;
    }
}
