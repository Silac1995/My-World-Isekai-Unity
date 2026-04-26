using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterNeeds : CharacterSystem, ICharacterSaveData<NeedsSaveData>
{
    private List<CharacterNeed> _allNeeds = new List<CharacterNeed>();
    public List<CharacterNeed> AllNeeds => _allNeeds;

    /// <summary>
    /// Typed accessor for needs. Returns null if no need of type T is registered.
    /// </summary>
    public T GetNeed<T>() where T : CharacterNeed
    {
        foreach (var need in _allNeeds)
        {
            if (need is T typed) return typed;
        }
        return null;
    }
    
    private NeedSocial _socialNeed;

    private void Start()
    {
        // Initialisation du besoin social
        _socialNeed = new NeedSocial(_character);
        _allNeeds.Add(_socialNeed);

        _allNeeds.Add(new NeedToWearClothing(_character));
        _allNeeds.Add(new NeedJob(_character));

        var hunger = new NeedHunger(_character);
        // Re-attempt subscription in case TimeManager wasn't ready in NeedHunger ctor.
        hunger.TrySubscribeToPhase();
        _allNeeds.Add(hunger);

        if (MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        }
    }

    private void HandleNewDay()
    {
        if (_socialNeed != null)
        {
            // Decays social need by 15 every in-game day
            _socialNeed.DecreaseValue(45f);
        }
    }

    private void OnDestroy()
    {
        var hungerNeed = GetNeed<NeedHunger>();
        if (hungerNeed != null) hungerNeed.UnsubscribeFromPhase();

        if (MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }

    // --- ICharacterSaveData IMPLEMENTATION ---

    public string SaveKey => "CharacterNeeds";
    public int LoadPriority => 40;

    public NeedsSaveData Serialize()
    {
        var data = new NeedsSaveData();

        foreach (var need in _allNeeds)
        {
            data.needs.Add(new NeedSaveEntry
            {
                needType = need.GetType().Name,
                value = need.CurrentValue
            });
        }

        return data;
    }

    public void Deserialize(NeedsSaveData data)
    {
        if (data == null || data.needs == null) return;

        foreach (var entry in data.needs)
        {
            var matchingNeed = _allNeeds.Find(n => n.GetType().Name == entry.needType);
            if (matchingNeed != null)
            {
                matchingNeed.CurrentValue = entry.value;
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[CharacterNeeds]</color> No matching need found for saved type '{entry.needType}' on {_character.CharacterName}.");
            }
        }
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
