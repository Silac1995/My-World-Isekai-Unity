using System.Collections.Generic;

public class BattleTeam
{
    private List<Character> charactersList = new List<Character>();
    public List<Character> CharacterList => charactersList;

    public void AddCharacter(Character character)
    {
        if (character == null)
            throw new System.ArgumentNullException(nameof(character), "Character cannot be null.");

        if (!charactersList.Contains(character))
            charactersList.Add(character);
    }

    public bool RemoveCharacter(Character character)
    {
        if (character == null)
            throw new System.ArgumentNullException(nameof(character), "Character cannot be null.");

        return charactersList.Remove(character);
    }

    public void InitializeToBattleManager(BattleManager manager)
    {
        if (manager == null)
            throw new System.ArgumentNullException(nameof(manager), "BattleManager cannot be null.");

        foreach (var character in charactersList)
        {
            character.JoinBattle(manager);
        }
    }
}
