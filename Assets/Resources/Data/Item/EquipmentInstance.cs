using UnityEngine;

public class EquipmentInstance : ItemInstance
{
    // Correction ici : on appelle le constructeur de ItemInstance avec : base(data)
    public EquipmentInstance(ItemSO data) : base(data)
    {
        // Tu n'as plus besoin de faire this.ItemSO = data;
        // C'est déjà géré par la classe parente !

        // Initialise tes stats spécifiques ici
        Debug.Log("Equipement créé avec succès");
    }

    public void EquipToCharacter(Character character)
    {
        character?.EquipGear(this);
    }
}