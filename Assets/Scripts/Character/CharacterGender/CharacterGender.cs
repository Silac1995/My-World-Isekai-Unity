using UnityEngine;

[System.Serializable] // Indispensable pour l'inspecteur
public class CharacterGender
{
    // Utilisation d'une propriété en lecture seule (format court)
    public virtual string GenderName => "Default";
}