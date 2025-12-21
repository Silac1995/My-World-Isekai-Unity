using System.Collections.Generic;
using UnityEngine;

public class Ability : ScriptableObject
{
    [SerializeField] private string abilityName;
    [SerializeField] private float cost; // 💬 Tu peux changer le type si c'est un coût numérique
    [SerializeField] private List<CharacterStatusEffect> characterStatusEffects;
    [SerializeField] private GameObject visualPrefab;

    public string AbilityName => abilityName;
    public float Cost => cost;
    public List<CharacterStatusEffect> CharacterStatusEffects => characterStatusEffects;
    public GameObject VisualPrefab => visualPrefab;
}


[CreateAssetMenu(menuName = "Abilities/Spell")]
public class Spell : Ability
{

}

[CreateAssetMenu(menuName = "Abilities/Skill")]
public class Skill : Ability
{

}
