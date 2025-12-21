using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
[CreateAssetMenu(fileName = "CharacterStatusEffect", menuName = "Character Status Effect")]
public class CharacterStatusEffect : ScriptableObject
{
    [SerializeField]
    private string statusEffectName;
    [SerializeField]
    private List<StatusEffect> statusEffects;
    [SerializeField]
    private float duration; // if equals 0 => permanent
    [SerializeField]
    private GameObject visualEffectPrefab;
    [SerializeField]
    private Sprite icon;
    [SerializeField]
    private string description;

    public string StatusEffectName => statusEffectName;
    public IReadOnlyList<StatusEffect> StatusEffects => statusEffects.AsReadOnly();
    public float Duration => duration;
    public GameObject VisualEffectPrefab => visualEffectPrefab;
    public Sprite Icon => icon;
    public string Description => description;
}
