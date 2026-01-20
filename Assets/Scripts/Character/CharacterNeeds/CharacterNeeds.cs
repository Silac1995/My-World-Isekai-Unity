using System.Collections.Generic;
using UnityEngine;

public class CharacterNeeds : MonoBehaviour
{
    [SerializeField] private Character _character;
    private List<CharacterNeed> _allNeeds = new List<CharacterNeed>();
    public List<CharacterNeed> AllNeeds => _allNeeds;

    private void Start()
    {
        // On enregistre tous les besoins possibles
        _allNeeds.Add(new NeedToWearClothing(_character));
        // _allNeeds.Add(new NeedHunger(_character)); // Exemple futur
    }

    private void Update()
    {
        // On ne vérifie pas à chaque frame pour les NPCs (optimisation multi)
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

        // Si on a trouvé un besoin critique et que l'IA ne fait rien d'important
        if (mostUrgent != null && npc.CurrentBehaviour is WanderBehaviour)
        {
            mostUrgent.Resolve(npc);
        }
    }
}