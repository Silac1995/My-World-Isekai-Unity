using System.Collections;
using UnityEngine;

public class CharacterActions : MonoBehaviour
{
    [SerializeField] private Character _character;
    public Character Character => _character;

    // Nouvelle variable pour suivre l'action en cours
    private CharacterAction _currentAction;
    public CharacterAction CurrentAction => _currentAction;

    private void Awake()
    {
        if (_character == null)
        {
            _character = GetComponent<Character>();
            if (_character == null)
            {
                Debug.LogError("Character non trouvé dans CharacterActions.", this);
                enabled = false;
            }
        }
    }

    public void ExecuteAction(CharacterAction action)
    {
        if (action == null) return;

        // 1. Vérifie si on est déjà occupé
        if (_currentAction != null)
        {
            Debug.Log($"{_character.CharacterName} est déjà occupé.");
            return;
        }

        _currentAction = action;

        // 2. On s'abonne pour libérer le slot à la fin
        _currentAction.OnActionFinished += () => _currentAction = null;

        // 3. On lance le début de l'action (Animation, etc.)
        _currentAction.OnStart();

        // 4. On lance le chrono pour l'effet et la fin
        StartCoroutine(ActionTimerRoutine(_currentAction));
    }

    private IEnumerator ActionTimerRoutine(CharacterAction action)
    {
        // On attend la durée définie dans l'action (ex: 1.2s)
        yield return new WaitForSeconds(action.Duration);

        // On applique l'effet (ex: AddItem)
        action.OnApplyEffect();

        // On déclenche le callback de fin
        action.Finish();
    }

    // Méthode pour libérer le personnage (à appeler à la fin de l'animation ou de l'action)
    public void ClearCurrentAction()
    {
        _currentAction = null;
    }

    private bool CanPerform(CharacterAction action)
    {
        // return !Character.IsStunned && Character.IsAlive;
        return true;
    }
}