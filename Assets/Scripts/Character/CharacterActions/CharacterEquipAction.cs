using UnityEngine;

public class CharacterEquipAction : CharacterAction
{
    private EquipmentInstance equipment;

    public CharacterEquipAction(Character character, EquipmentInstance equipment) : base(character)
    {
        this.equipment = equipment ?? throw new System.ArgumentNullException(nameof(equipment));
    }

    public override void PerformAction()
    {
        // On vérifie une dernière fois si l'équipement est valide
        if (equipment == null || equipment.ItemSO == null)
        {
            Debug.LogWarning($"{character.CharacterName} tente d'équiper un objet invalide.");
            return;
        }

        // On appelle la méthode de logique sur le Character
        character.EquipGear(equipment);

        Debug.Log($"{character.CharacterName} a équipé : {equipment.CustomizedName}");
    }
}