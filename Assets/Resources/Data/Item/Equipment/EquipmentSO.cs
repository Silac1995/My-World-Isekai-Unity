using UnityEngine;

[CreateAssetMenu(fileName = "Equipment", menuName = "Scriptable Objects/Equipment")]
public class EquipmentSO : ItemSO
{
    [Header("Category")]
    public string category_name;

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
}
