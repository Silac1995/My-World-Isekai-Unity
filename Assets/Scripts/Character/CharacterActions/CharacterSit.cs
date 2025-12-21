using UnityEngine;
using UnityEngine.TextCore.Text;

public class CharacterSit : CharacterAction
{
    public CharacterSit(Character character) : base(character) { }

    public override void PerformAction()
    {
        // Ici on pourrait jouer une animation
        Debug.Log($"{character.CharacterName} s'assoit.");
    }
}
