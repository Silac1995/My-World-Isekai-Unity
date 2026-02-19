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
        if (npc == null || !npc.enabled) return;

        // Seul le WanderBehaviour peut être interrompu par un besoin
        if (!(npc.CurrentBehaviour is WanderBehaviour)) return;

        // 1. Filtrer les besoins actifs et les trier par urgence décroissante
        var sortedActiveNeeds = _allNeeds
            .Where(n => n.IsActive())
            .OrderByDescending(n => n.GetUrgency())
            .ToList();

        // 2. Tenter de résoudre chaque besoin, dans l'ordre, jusqu'à ce qu'un réussisse
        foreach (var need in sortedActiveNeeds)
        {
            if (need.Resolve(npc))
            {
                Debug.Log($"<color=orange>[Needs]</color> {npc.name} résout le besoin : {need.GetType().Name} (Urgence: {need.GetUrgency()})");
                break; // On a trouvé une action à faire, on arrête de chercher pour ce cycle
            }
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
            }
        }
    }
}
