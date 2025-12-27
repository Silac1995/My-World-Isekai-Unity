using UnityEngine;

public class CharacterUseConsumableAction : CharacterAction
{
    private ConsumableInstance item;

    public CharacterUseConsumableAction(Character character, ConsumableInstance item) : base(character)
    {
        this.item = item;
    }

    public override void PerformAction()
    {
        // La logique de consommation est déléguée au Character ou à l'item
        character.UseConsumable(item);

        // On peut ajouter ici des effets globaux (son, particules)
        Debug.Log($"{character.CharacterName} a consommé {item.CustomizedName}.");
    }
}