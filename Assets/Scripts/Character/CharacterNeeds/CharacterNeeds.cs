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
    }

    private void Update()
    {
        // On gère la perte des besoins à chaque frame (ou via leur propre timer interne)
        if (_socialNeed != null)
        {
            _socialNeed.UpdateValue();
        }

        // Optimization: Check only every 30 frames
        if (Time.frameCount % 30 != 0) return;

        // Si un BT est présent, il gère la résolution des besoins via BTCond_HasUrgentNeed
        var npc = _character.Controller as NPCController;
        if (npc != null && npc.HasBehaviourTree) return;

        EvaluateNeeds();
    }

    private void EvaluateNeeds()
    {
        var npc = _character.Controller as NPCController;
        if (npc == null || !npc.enabled) return;

        // Seul le WanderBehaviour peut être interrompu par un besoin
        if (!(npc.CurrentBehaviour is WanderBehaviour)) return;

        // Get active needs sorted by urgency
        var activeNeeds = _allNeeds
            .Where(n => n.IsActive())
            .OrderByDescending(n => n.GetUrgency())
            .ToList();

        foreach (var need in activeNeeds)
        {
            if (need.Resolve(npc))
            {
                Debug.Log($"<color=orange>[Needs]</color> {npc.name} résout le besoin : {need.GetType().Name} (Urgence: {need.GetUrgency()})");
                return;
            }
        }
    }
}
