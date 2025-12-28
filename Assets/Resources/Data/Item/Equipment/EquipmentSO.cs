using UnityEngine;

[CreateAssetMenu(fileName = "Equipment", menuName = "Scriptable Objects/Equipment")]
public class EquipmentSO : ItemSO
{
    [SerializeField] private EquipmentType equipmentType;
    [SerializeField] private EquipmentLayerEnum equipmentLayer;
    public EquipmentType EquipmentType => equipmentType;
    public EquipmentLayerEnum EquipmentLayer => equipmentLayer;

    // === Primary Stats ===
    [Header("Primary Stats")]
    public CharacterArmor armor;
    public CharacterHealth health;
    public CharacterStamina stamina;
    public CharacterMana mana;
    public CharacterInitiative initiative;

    // === Secondary Stats ===
    [Header("Secondary Stats")]
    public CharacterStrength strength;
    public CharacterAgility agility;
    public CharacterDexterity dexterity;
    public CharacterIntelligence intelligence;
    public CharacterEndurance endurance;

    // On force le type EquipmentInstance
    public override System.Type InstanceType => typeof(EquipmentInstance);
    public override ItemInstance CreateInstance()
    {
        return new EquipmentInstance(this);
    }


}
