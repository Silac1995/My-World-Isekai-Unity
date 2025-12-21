using UnityEngine;

public class CharacterInteractable : InteractableObject
{
    [SerializeField] private Character character;

    public Character Character => character;

    public override void Interact()
    {
        // Interaction avec ce Character
    }
}
