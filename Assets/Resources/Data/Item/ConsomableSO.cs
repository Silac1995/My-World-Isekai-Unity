using System.Collections.Generic; // Obligatoire pour utiliser les List
using UnityEngine;

[CreateAssetMenu(fileName = "ConsumableSO", menuName = "Scriptable Objects/ConsumableSO")]
public class ConsomableSO : ItemSO
{
    [Header("Consumable Settings")]
    [SerializeField] private bool destroyOnUse = true;

    // Correction de la syntaxe de la liste
    [SerializeField] private List<string> effects = new List<string>();

    // Getters
    public bool DestroyOnUse => destroyOnUse;
    public List<string> Effects => effects;

    // On force le type BagInstance
    public override System.Type InstanceType => typeof(ConsumableInstance);
    public override ItemInstance CreateInstance()
    {
        return new ConsumableInstance(this);
    }
}