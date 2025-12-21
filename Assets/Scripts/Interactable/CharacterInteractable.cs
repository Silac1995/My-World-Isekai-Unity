using UnityEngine;

public class CharacterInteractable : InteractableObject
{
    [SerializeField] private Character character; //self character
    public Character Character { get; set; }

    public void Awake()
    {
        character = GetComponentInParent<Character>();
    }
    public override void Interact()
    {
    }
}
