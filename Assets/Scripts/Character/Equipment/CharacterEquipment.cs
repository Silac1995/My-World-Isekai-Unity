using UnityEngine;

public class CharacterEquipment : MonoBehaviour
{
    [SerializeField] private Character character;

    [SerializeField] private Armor armor;
    [SerializeField] private Clothing clothing;
    [SerializeField] private Underwear underwear;

    public Character Character
    {
        get => character;
        set => character = value;
    }

    public Armor Armor
    {
        get => armor;
        set => armor = value;
    }

    public Clothing Clothing
    {
        get => clothing;
        set => clothing = value;
    }

    public Underwear Underwear
    {
        get => underwear;
        set => underwear = value;
    }

    public void equip(Equipment equipment)
    {

    }
}
