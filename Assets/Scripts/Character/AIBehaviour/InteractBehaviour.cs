using UnityEngine;
using UnityEngine.AI;

public class InteractBehaviour : IAIBehaviour
{
    private bool _hasStoppedAgent = false;

    public void Act(Character self)
    {
        Character target = self.CharacterInteraction.CurrentTarget;

        // Si la cible disparaît ou si l'interaction est rompue, 
        // le Controller s'occupera de changer le Behaviour.
        if (target == null) return;

        // 1. Logique visuelle : Le personnage fait face à sa cible
        Vector3 direction = target.transform.position - self.transform.position;
        if (self.CharacterVisual != null)
        {
            self.CharacterVisual.UpdateFlip(direction);
        }

        // 2. Logique de mouvement : On stoppe l'agent une seule fois
        if (!_hasStoppedAgent)
        {
            var agent = self.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.velocity = Vector3.zero; // Stop immédiat pour le rig
            }
            _hasStoppedAgent = true;
        }
    }

    public void Exit(Character self)
    {
        // On s'assure que les flags sont réinitialisés
        _hasStoppedAgent = false;

        Debug.Log($"{self.CharacterName} sort de l'état d'interaction.");
    }
}