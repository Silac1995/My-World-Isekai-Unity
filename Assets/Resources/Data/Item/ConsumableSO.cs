using System.Collections.Generic; // Obligatoire pour utiliser les List
using UnityEngine;

[CreateAssetMenu(fileName = "ConsumableSO", menuName = "Scriptable Objects/Items/Consumable")]
public class ConsumableSO : MiscSO // Hérite de MiscSO
{
    [Header("Consumable Settings")]
    [SerializeField] private bool _destroyOnUse = true;
    [SerializeField] private List<string> effects = new List<string>();

    public bool DestroyOnUse => _destroyOnUse;
    public List<string> Effects => effects;

    public override System.Type InstanceType => typeof(ConsumableInstance);
    public override ItemInstance CreateInstance() => new ConsumableInstance(this);
}