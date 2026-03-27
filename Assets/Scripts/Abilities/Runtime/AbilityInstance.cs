using UnityEngine;

[System.Serializable]
public abstract class AbilityInstance
{
    [SerializeField] protected AbilitySO _data;
    protected Character _owner;

    public AbilitySO Data => _data;
    public Character Owner => _owner;
    public string AbilityId => _data != null ? _data.AbilityId : string.Empty;

    public AbilityInstance(AbilitySO data, Character owner)
    {
        _data = data;
        _owner = owner;
    }

    /// <summary>
    /// Can this ability be used right now? Checks resource costs, cooldowns, weapon requirements, etc.
    /// </summary>
    public abstract bool CanUse(Character target);
}
