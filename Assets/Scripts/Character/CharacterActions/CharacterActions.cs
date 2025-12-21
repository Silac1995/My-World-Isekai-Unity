using UnityEngine;

public class CharacterActions : MonoBehaviour
{
    [SerializeField] private Character character;
    public Character Character => character;

    private void Awake()
    {
        if (Character == null)
        {
            character = GetComponent<Character>();
            if (Character == null)
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

        if (!CanPerform(action))
        {
            Debug.Log($"{Character.CharacterName} ne peut pas effectuer {action.GetType().Name}.");
            return;
        }

        action.PerformAction();

        Debug.Log($"{Character.CharacterName} exécute {action.GetType().Name}.");
    }

    private bool CanPerform(CharacterAction action)
    {
        // Exemples de conditions
        // return !Character.IsStunned && Character.IsAlive;
        return true;
    }
}
