using UnityEngine;

public class CharacterEquipAction : CharacterAction
{
    private EquipmentInstance _equipment;

    // On ajoute un délai de 0.8s pour l'action d'équipement
    public CharacterEquipAction(Character character, EquipmentInstance equipment)
        : base(character, 0.8f)
    {
        _equipment = equipment ?? throw new System.ArgumentNullException(nameof(equipment));
    }

    public override void OnStart()
    {
        if (_equipment == null) { Finish(); return; }

        // 1. On lance une animation de "Prepare" ou on joue un son d'armure
        var animator = character.CharacterVisual?.CharacterAnimator?.Animator;
        if (animator != null) animator.SetTrigger("Trigger_Equip");

        Debug.Log($"{character.CharacterName} prépare l'équipement de : {_equipment.CustomizedName}");
    }

    public override void OnApplyEffect()
    {
        // 2. La logique métier s'exécute APRÈS le délai de 0.8s
        character.CharacterEquipment.Equip(_equipment);

        // On met à jour l'Animator si c'est une arme pour changer le style de combat
        // (comme on en a discuté avec le CombatStyleSO)
    }
}