using UnityEngine;

public class NPCInteractionDetector : CharacterInteractionDetector
{
    private float interactionCooldown = 2f; // Cooldown pour éviter des interactions répétées
    private float lastInteractionTime;

    private void Update()
    {
        if (_currentInteractableObjectTarget == null) return;

        // On garde la rotation vers la cible (c'est joli visuellement)
        Vector3 direction = _currentInteractableObjectTarget.transform.position - transform.position;
        direction.y = 0f;
        if (direction.magnitude > 0.1f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 3f);
        }

        // --- SUPPRIME OU COMMENTE LA LOGIQUE D'INTERACTION AUTOMATIQUE ICI ---
        // Un NPC ne doit pas "décider" d'interagir juste parce qu'il touche un trigger,
        // c'est son IA (Need/Behaviour) qui doit donner l'ordre.
    }
}