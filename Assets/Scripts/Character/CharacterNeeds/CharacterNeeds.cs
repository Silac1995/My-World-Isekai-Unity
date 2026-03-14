using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CharacterNeeds : MonoBehaviour
{
    [SerializeField] private Character _character;
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

        StartCoroutine(SocialDecayCoroutine());
    }

    private IEnumerator SocialDecayCoroutine()
    {
        while (true)
        {
            // Decays social need outside of per-frame polling
            yield return new WaitForSeconds(1f);
            if (_socialNeed != null)
            {
                _socialNeed.DecreaseValue(3f);
            }
        }
    }
}
