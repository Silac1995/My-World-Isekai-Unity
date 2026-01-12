using UnityEngine;

[System.Serializable]
public class CharacterBio
{
    [SerializeField] private Character _character;
    [SerializeField] private CharacterGender _characterGender;
    [SerializeField] private int _age;
    [SerializeField] private float _weight;
    [SerializeField] private float _height;

    // Propriétés publiques
    public Character Character => _character;
    public CharacterGender Gender => _characterGender;
    public int Age => _age;
    public float Weight => _weight;
    public float Height => _height;

    public bool IsMale => _characterGender is CharacterGenderMale;
    public bool IsFemale => _characterGender is CharacterGenderFemale;

    // Constructeur demandé
    public CharacterBio(Character character, GenderType type, int age = 1)
    {
        _character = character;
        _age = age;

        // Initialisation du genre basée sur l'enum
        if (type == GenderType.Male)
            SetGenderToMale();
        else
            SetGenderToFemale();

        // Valeurs par défaut pour le reste
        _weight = 3.5f; // Poids bébé par défaut
        _height = 0.5f; // Taille bébé par défaut
    }

    public void SetGenderToMale()
    {
        _characterGender = new CharacterGenderMale();
    }

    public void SetGenderToFemale()
    {
        _characterGender = new CharacterGenderFemale();
    }
}