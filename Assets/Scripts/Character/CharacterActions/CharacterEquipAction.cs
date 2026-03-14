using UnityEngine;

public class CharacterEquipAction : CharacterAction
{
    private EquipmentInstance _equipment;

    // On ajoute un dlai de 0.8s pour l'action d'quipement
    public CharacterEquipAction(Character character, EquipmentInstance equipment)
        : base(character, 0.8f)
    {
        _equipment = equipment ?? throw new System.ArgumentNullException(nameof(equipment));
    }

    public override void OnStart()
    {
        if (_equipment == null) { Finish(); return; }

        Debug.Log($"{character.CharacterName} prpare l'quipement...");
    }

    public override void OnApplyEffect()
    {
        character.CharacterEquipment.Equip(_equipment);
    }
}
