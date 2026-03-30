/// <summary>
/// Serializable DTO for CharacterProfile save/load.
/// Stores core identity data: race, gender, age, bio, visual seed, archetype, personality.
/// </summary>
[System.Serializable]
public class ProfileSaveData
{
    public string raceId;
    public int gender;
    public int age;
    public float weight;
    public float height;
    public string characterName;
    public int visualSeed;
    public string archetypeId;
    public string personalityId;
}
