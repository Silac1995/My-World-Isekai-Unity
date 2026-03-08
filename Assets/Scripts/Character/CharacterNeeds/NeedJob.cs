using System.Linq;
using UnityEngine;

public class NeedJob : CharacterNeed
{
    // L'urgence peut varier en fonction de la condition du PNJ (Richesse, Faim, etc.) 
    // ou être fixe à 60 (Moyennement urgent, moins que la survie, plus que le blabla).
    private const float BASE_URGENCY = 60f;

    public NeedJob(Character character) : base(character)
    {
    }

    public override bool IsActive()
    {
        // Actif si le personnage est un PNJ ET qu'il n'a pas de job.
        // Optionnel : On peut exclure certaines classes (Enfants, Nobles, etc.)
        if (_character.Controller is PlayerController) return false;
        
        return _character.CharacterJob != null && !_character.CharacterJob.HasJob;
    }

    public override float GetUrgency()
    {
        return BASE_URGENCY;
    }

    public override bool Resolve(NPCController npc)
    {
        if (BuildingManager.Instance == null || BuildingManager.Instance.allBuildings.Count == 0)
        {
            return false;
        }

        // 1. Chercher d'abord un business vide pour devenir propriétaire
        CommercialBuilding unownedCommercial = BuildingManager.Instance.FindUnownedCommercialBuilding();
        if (unownedCommercial != null && _character.CharacterJob.BecomeOwner(unownedCommercial))
        {
            // Le personnage a pris possession du commerce
            return true;
        }

        // 2. Chercher n'importe quel job disponible dans un CommercialBuilding (qui a un patron valide et physiquement là)
        foreach (var building in BuildingManager.Instance.allBuildings)
        {
            if (building is CommercialBuilding commercial && commercial.HasOwner)
            {
                Character boss = commercial.Owner;
                
                // Le boss doit être instancié, en vie et libre pour qu'on aille lui parler
                if (boss != null && boss.IsAlive() && boss.IsFree())
                {
                    var availableJobs = commercial.GetAvailableJobs();
                    if (availableJobs != null && availableJobs.Any())
                    {
                        var desiredJob = availableJobs.First();

                        Debug.Log($"<color=cyan>[NeedJob]</color> {_character.CharacterName} va demander le poste de {desiredJob.JobTitle} à {boss.CharacterName}.");

                        // On lance le comportement pour aller physiquement lui parler
                        npc.PushBehaviour(new MoveToTargetBehaviour(npc, boss.gameObject, 2.5f, () =>
                        {
                            if (boss == null || !boss.IsAlive() || !boss.IsFree()) return;

                            // Une fois arrivé devant le boss, on déclenche l'interaction de demande d'emploi
                            npc.Character.CharacterInteraction.StartInteractionWith(boss, new InteractionAskForJob(commercial, desiredJob));
                        }));

                        return true;
                    }
                }
            }
        }

        // Aucun job trouvé
        return false;
    }
}
