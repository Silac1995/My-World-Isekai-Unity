using System.Collections.Generic;
using System.Linq;

[System.Serializable]
public class BattleTeam
{
    [UnityEngine.SerializeField] private List<Character> _charactersList = new List<Character>();
    public List<Character> CharacterList => _charactersList;

    public void AddCharacter(Character character)
    {
        if (character != null && !_charactersList.Contains(character))
            _charactersList.Add(character);
    }

    /// <summary>
    /// Checks whether the given character belongs to this team (is an ally).
    /// </summary>
    public bool IsAlly(Character character)
    {
        if (character == null) return false;
        return _charactersList.Contains(character);
    }

    /// <summary>
    /// Checks whether the character belongs to an OPPOSING team in the same combat.
    /// </summary>
    public bool IsOpponent(Character character, BattleManager manager)
    {
        if (character == null || manager == null) return false;
        // We check if ANY OTHER team in this manager contains the character
        return manager.BattleTeams.Any(team => team != this && team.IsAlly(character));
    }

    public bool ContainsCharacter(Character character) => IsAlly(character);

    public Character GetRandomMember()
    {
        List<Character> aliveMembers = _charactersList.FindAll(c => c != null && c.IsAlive());

        if (aliveMembers.Count == 0) return null;

        int randomIndex = UnityEngine.Random.Range(0, aliveMembers.Count);
        return aliveMembers[randomIndex];
    }

    public Character GetClosestMember(UnityEngine.Vector3 position)
    {
        Character closest = null;
        float minDistance = float.MaxValue;

        foreach (var c in _charactersList)
        {
            if (c == null || !c.IsAlive()) continue;

            float distance = UnityEngine.Vector3.Distance(position, c.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = c;
            }
        }

        return closest;
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
