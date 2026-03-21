using System.Linq;
using UnityEngine;

/// <summary>
/// CharacterAction pour récolter un Harvestable.
/// Le personnage joue une animation de récolte, attend la durée,
/// puis récolte l'item du Harvestable.
/// </summary>
public class CharacterHarvestAction : CharacterAction
{
    private Harvestable _target;
    private ItemSO _harvestedItem;

    /// <summary>L'item récolté après l'action (null si pas encore fini)</summary>
    public ItemSO HarvestedItem => _harvestedItem;

    public CharacterHarvestAction(Character character, Harvestable target)
        : base(character, target != null ? target.HarvestDuration : 1f)
    {
        _target = target;
    }

    public override bool CanExecute()
    {
        if (_target == null || !_target.CanHarvest())
        {
            Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} ne peut pas récolter : cible invalide ou épuisée.");
            return false;
        }

        // Vérification de la portée via l'InteractionZone (Collider) de l'objet
        if (_target.InteractionZone != null)
        {
            if (!_target.InteractionZone.bounds.Contains(character.transform.position))
            {
                // Fallback : On vérifie si on est au moins très proche du collider (pour les petits colliders ou offsets)
                float dist = Vector3.Distance(character.transform.position, _target.InteractionZone.bounds.ClosestPoint(character.transform.position));
                if (dist > 2.5f)
                {
                    Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} est trop loin de la zone d'interaction de {_target.gameObject.name} (Dist: {dist}).");
                    return false;
                }
            }
        }
        else
        {
            // Fallback si pas de zone : distance fixe
            float dist = Vector3.Distance(character.transform.position, _target.transform.position);
            if (dist > 3f)
            {
                Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} est trop loin pour récolter {_target.gameObject.name} (pas de zone d'interaction).");
                return false;
            }
        }

        return true;
    }

    public override void OnStart()
    {
        Debug.Log($"<color=cyan>[Harvest Action]</color> {character.CharacterName} commence à récolter {_target.gameObject.name}...");
    }

    public override void OnApplyEffect()
    {
        if (_target == null || !_target.CanHarvest())
        {
            Debug.LogWarning($"<color=orange>[Harvest Action]</color> {character.CharacterName} : la cible a disparu ou est épuisée.");
            return;
        }

        // Récolter l'item (retourne le ItemSO)
        _harvestedItem = _target.Harvest(character);

        if (_harvestedItem != null)
        {
            // Spawn le WorldItem au sol devant le personnage
            Vector3 spawnPos = character.transform.position + character.transform.forward * 0.5f + Vector3.up * 0.3f;
            WorldItem spawnedItem = WorldItem.SpawnWorldItem(_harvestedItem, spawnPos);
            
            // Inscrire la ressource au sol comme tâche pour le bâtiment
            if (spawnedItem != null && character.CharacterJob != null)
            {
                var workAssignment = character.CharacterJob.ActiveJobs.FirstOrDefault(j => j.AssignedJob is JobHarvester);
                if (workAssignment != null && workAssignment.Workplace != null)
                {
                    workAssignment.Workplace.TaskManager?.RegisterTask(new PickupLooseItemTask(spawnedItem));
                }
            }
        }
    }

    public override void OnCancel()
    {
        base.OnCancel();
        Debug.Log($"<color=orange>[Harvest Action]</color> {character.CharacterName} a annulé sa récolte.");
    }
}
