using UnityEngine;

public class CharacterActions : MonoBehaviour
{
    [SerializeField] private Character character;
    public Character Character => character;

    // Nouvelle variable pour suivre l'action en cours
    private CharacterAction currentAction;
    public CharacterAction CurrentAction => currentAction;

    private void Awake()
    {
        if (character == null)
        {
            character = GetComponent<Character>();
            if (character == null)
            {
                Debug.LogError("Character non trouvé dans CharacterActions.", this);
                enabled = false;
            }
        }
    }

    public void PerformAction(CharacterAction action)
    {
        if (action == null)
        {
            Debug.LogWarning("Tentative de perform une action nulle.", this);
            return;
        }

        // 1. Vérifie si on ne fait pas déjà quelque chose
        if (currentAction != null)
        {
            Debug.Log($"{Character.CharacterName} est déjà occupé avec {currentAction.GetType().Name}.");
            return;
        }

        // 2. Vérifie les conditions (stun, mort, etc.)
        if (!CanPerform(action))
        {
            Debug.Log($"{Character.CharacterName} ne peut pas effectuer {action.GetType().Name}.");
            return;
        }

        // 3. Assigne et exécute
        currentAction = action;

        // On exécute l'action
        // Note : Il faudra prévoir un moyen dans CharacterAction de remettre currentAction à null quand c'est fini !
        action.PerformAction();

        Debug.Log($"{Character.CharacterName} exécute {action.GetType().Name}.");
    }

    // Méthode pour libérer le personnage (à appeler à la fin de l'animation ou de l'action)
    public void ClearCurrentAction()
    {
        currentAction = null;
    }

    private bool CanPerform(CharacterAction action)
    {
        // return !Character.IsStunned && Character.IsAlive;
        return true;
    }
}