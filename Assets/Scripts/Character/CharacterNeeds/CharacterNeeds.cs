using System.Collections.Generic;
using UnityEngine;

public class CharacterNeeds : MonoBehaviour
{
    [SerializeField] private Character _character;
    private List<CharacterNeed> _allNeeds = new List<CharacterNeed>();
    public List<CharacterNeed> AllNeeds => _allNeeds;
    // Ton nouvel attribut privé
    private NeedSocial _socialNeed;

    private void Start()
    {
        // Initialisation du besoin social
        _socialNeed = new NeedSocial(_character);
        _allNeeds.Add(_socialNeed);

        _allNeeds.Add(new NeedToWearClothing(_character));
    }

    private void Update()
    {
        // 1. Mise à jour "physique" des besoins (la barre descend)
        _socialNeed.UpdateValue();

        // 2. Évaluation de l'IA (toutes les 30 frames)
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
}