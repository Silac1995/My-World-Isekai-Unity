using System.Collections.Generic;
using UnityEngine;

public class Bedroom : Room
{
    [Header("Bedroom Info")]
    [SerializeField] private List<Character> _owners = new List<Character>();

    public IReadOnlyList<Character> Owners => _owners;

    public void AddOwner(Character character)
    {
        if (!_owners.Contains(character))
        {
            _owners.Add(character);
        }
    }

    public void RemoveOwner(Character character)
    {
        if (_owners.Contains(character))
        {
            _owners.Remove(character);
        }
    }

    public bool IsOwner(Character character)
    {
        return _owners.Contains(character);
    }
}
