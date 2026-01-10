using UnityEngine;

[System.Serializable]
public class CharacterBodyPart
{
    [Header("Base References")]
    // On utilise l'underscore pour le privé comme convenu
    [SerializeField] protected CharacterBodyPartsController _bodyPartsController;

    // Constructeur pour passer la référence du controller
    public CharacterBodyPart(CharacterBodyPartsController controller)
    {
        _bodyPartsController = controller;
    }

    // Getter pour accéder au controller si besoin
    public CharacterBodyPartsController BodyPartsController => _bodyPartsController;
}