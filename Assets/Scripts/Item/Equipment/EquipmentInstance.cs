using UnityEngine;

public abstract class EquipmentInstance : ItemInstance
{
    // On rend le constructeur protégé car on ne peut plus instancier un "Equipement" pur
    protected EquipmentInstance(ItemSO data) : base(data)
    {
        Debug.Log($"<color=white>[Equipment]</color> Base de l'équipement {ItemSO.ItemName} initialisée.");
    }

    // Cette méthode peut être surchargée si l'équipement a une logique spéciale au moment du clic
    public virtual void EquipToCharacter(Character character)
    {
        character?.CharacterEquipment.Equip(this);
    }
}