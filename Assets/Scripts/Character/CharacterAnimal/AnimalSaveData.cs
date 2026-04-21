using System;

/// <summary>
/// Persistent portion of CharacterAnimal state — rides in the NPC hibernation bundle
/// via CharacterDataCoordinator. IsTameable and TameDifficulty are NOT saved here;
/// they are re-seeded from the archetype on respawn.
/// </summary>
[Serializable]
public class AnimalSaveData
{
    public bool IsTamed;
    public string OwnerProfileId;
}
