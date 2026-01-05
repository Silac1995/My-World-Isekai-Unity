using UnityEngine;

public class NPCInteractionDetector : CharacterInteractionDetector
{
    private float interactionCooldown = 2f; // Cooldown pour éviter des interactions répétées
    private float lastInteractionTime;

    private void Update()
    {
        if (currentTarget == null)
        {
            return;
        }

        // Rotation vers la cible
        Vector3 direction = currentTarget.transform.position - transform.position;
        direction.y = 0f;

        if (direction.magnitude > 0.1f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 3f);
            //Debug.Log($"NPC tourne vers {currentTarget.name}.", this);
        }

        // Interaction avec la cible (avec cooldown)
        if (Time.time - lastInteractionTime > interactionCooldown)
        {
            try
            {
                // Vérifier si la cible est un CharacterInteractable
                if (currentTarget.TryGetComponent(out CharacterInteractable characterInteractable))
                {
                    if (characterInteractable.Character == null)
                    {
                        Debug.LogWarning($"CharacterInteractable sur {currentTarget.name} a un champ 'character' null.", this);
                        return;
                    }
                    //currentTarget.Interact();
                    //Debug.Log($"NPC interagit avec {characterInteractable.Character.name}.", this);
                }
                else
                {
                    // Interaction avec un InteractableObject générique (si souhaité)
                    currentTarget.Interact(Character);
                    Debug.Log($"NPC interagit avec l'objet générique {currentTarget.name}.", this);
                }
                lastInteractionTime = Time.time;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Erreur lors de l'interaction avec {currentTarget.name} : {ex.Message}", this);
            }
        }
    }
}