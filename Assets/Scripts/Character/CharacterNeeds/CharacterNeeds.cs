using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterNeeds : CharacterSystem
{
    private List<CharacterNeed> _allNeeds = new List<CharacterNeed>();
    public List<CharacterNeed> AllNeeds => _allNeeds;
    
    private NeedSocial _socialNeed;

    private void Start()
    {
        // Initialisation du besoin social
        _socialNeed = new NeedSocial(_character);
        _allNeeds.Add(_socialNeed);

        _allNeeds.Add(new NeedToWearClothing(_character));
        _allNeeds.Add(new NeedJob(_character));

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
        if (MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }
}
