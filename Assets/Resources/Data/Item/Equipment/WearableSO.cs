using UnityEngine;

[CreateAssetMenu(fileName = "New Wearable", menuName = "Scriptable Objects/Equipment/Wearable")]
public class WearableSO : EquipmentSO
{
    [Header("Base Equipment Settings")]
    [SerializeField] readonly protected EquipmentCategory _equipmentCategory = EquipmentCategory.Wearable;

    // On le met en protected pour que BagSO y ait accès
    [SerializeField] protected WearableLayerEnum _equipmentLayer;

    public WearableLayerEnum EquipmentLayer => _equipmentLayer;
    [Header("Wearable Specifics")]
    [SerializeField] private WearableType _wearableType;

    public WearableType WearableType => _wearableType;

    // On peut forcer la catégorie dans le constructeur ou via OnValidate
    private void OnValidate()
    {
        // Utile pour que l'inspecteur affiche toujours la bonne catégorie
        // sans que tu puisses la changer manuellement par erreur.
        // (Nécessite que _equipmentCategory soit protected ou via une propriété)
    }

    public override System.Type InstanceType => typeof(WearableInstance);
    public override ItemInstance CreateInstance() => new WearableInstance(this);
}