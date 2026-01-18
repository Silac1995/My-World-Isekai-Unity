using System.Collections.Generic;

public class BattleTeam
{
    private List<Character> _charactersList = new List<Character>();
    public List<Character> CharacterList => _charactersList;

    public void AddCharacter(Character character)
    {
        if (character != null && !_charactersList.Contains(character))
            _charactersList.Add(character);
    }

    public bool IsTeamEliminated()
    {
        if (_charactersList.Count == 0) return true;
        foreach (var c in _charactersList)
        {
            if (c != null && c.IsAlive()) return false;
        }
        return true;
    }
}