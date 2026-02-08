using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterNeeds : MonoBehaviour
{
    [SerializeField] private Character _character;
    private List<CharacterNeed> _allNeeds = new List<CharacterNeed>();
    public List<CharacterNeed> AllNeeds => _allNeeds;
    // Ton nouvel attribut privé
    private NeedSocial _socialNeed;
    private Coroutine _socialCoroutine;

    private void Start()
    {
        // Initialisation du besoin social
        _socialNeed = new NeedSocial(_character);
        _allNeeds.Add(_socialNeed);

        _allNeeds.Add(new NeedToWearClothing(_character));
    }

    private void Update()
    {
        if (Time.frameCount % 30 != 0) return;
        EvaluateNeeds();
    }

    private void EvaluateNeeds()
    {
        var npc = _character.Controller as NPCController;
        if (npc == null) return;

        CharacterNeed mostUrgent = null;
        float maxUrgency = 0f;

        foreach (var need in _allNeeds)
        {
            if (need.IsActive())
            {
                float urgency = need.GetUrgency();
                if (urgency > maxUrgency)
                {
                    maxUrgency = urgency;
                    mostUrgent = need;
                }
            }
        }

        // Si le besoin le plus urgent est le social, Resolve va lancer un Follow
        if (mostUrgent != null && npc.CurrentBehaviour is WanderBehaviour)
        {
            mostUrgent.Resolve(npc);
        }
    }

    

    private void OnEnable()
    {
        _socialCoroutine = StartCoroutine(SocialTickCoroutine());
    }

    private void OnDisable()
    {
        if (_socialCoroutine != null)
        {
            StopCoroutine(_socialCoroutine);
            _socialCoroutine = null;
        }
    }

    private IEnumerator SocialTickCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);
            if (_socialNeed != null)
            {
                _socialNeed.DecreaseValue(2.5f);
                // Debug.Log($"Tick Social : {_character.CharacterName}");
            }
        }
    }
}