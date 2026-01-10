using UnityEngine;

[System.Serializable]
public class CharacterPubicHair : CharacterHair
{
    // Hérite de tout CharacterHair. 
    // Tu peux ajouter ici des spécificités (ex: lien avec les sous-vêtements)
    public CharacterPubicHair(CharacterBodyPartsController controller, GameObject obj, string category, string label)
        : base(controller, obj, category, label)
    {
    }
}