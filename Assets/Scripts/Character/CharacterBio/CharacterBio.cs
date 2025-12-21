using UnityEngine;

public class CharacterBio
{
    private Character character;
    public Character Character => character;

    private CharacterGender characterGender;
    public CharacterGender Gender => characterGender;

    private int age;
    public int Age => age;

    private float weight;
    public float Weight => weight;

    private float height;
    public float Height => height;

    public void SetGenderToMale()
    {
        characterGender = new CharacterGenderMale();
    }

    public void SetGenderToFemale()
    {
        characterGender = new CharacterGenderFemale();
    }

}
