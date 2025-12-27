using UnityEngine;
using UnityEngine.TextCore.Text;

public class CharacterDropItem : CharacterAction
{
    private ItemInstance itemInstance;

    public CharacterDropItem(Character character, ItemInstance item) : base(character)
    {
        this.itemInstance = item ?? throw new System.ArgumentNullException(nameof(item));
    }
    public override void PerformAction()
    {

    }
}
