using System.Collections;
using UnityEngine;

public class CharacterActions : MonoBehaviour
{
    [SerializeField] private Character _character;
    private CharacterAction _currentAction;
    private Coroutine _actionRoutine; // Référence pour éviter les accumulations

    public CharacterAction CurrentAction => _currentAction;

    public void ExecuteAction(CharacterAction action)
    {
        if (action == null) return;

        // 1. Vérifie si on est déjà occupé
        if (_currentAction != null) return;

        // 2. NOUVEAU : Vérifie si l'action est possible selon ses propres règles
        if (!action.CanExecute())
        {
            Debug.Log($"<color=red>[Actions]</color> {action.GetType().Name} impossible à exécuter.");
            return;
        }

        _currentAction = action;
        _currentAction.OnActionFinished += CleanupAction;

        _currentAction.OnStart();
        _actionRoutine = StartCoroutine(ActionTimerRoutine(_currentAction));
    }

    private IEnumerator ActionTimerRoutine(CharacterAction action)
    {
        // On attend la durée prévue
        yield return new WaitForSeconds(action.Duration);

        // On applique l'effet
        action.OnApplyEffect();

        // On termine l'action (ce qui déclenchera CleanupAction via l'event)
        action.Finish();
    }

    private void CleanupAction()
    {
        // On nettoie les références pour libérer la mémoire et permettre la suite
        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction; // Important : se désabonner
        }

        _currentAction = null;
        _actionRoutine = null; // La coroutine est finie, on oublie la référence
    }

    // Remplace ou ajoute cette méthode dans CharacterActions.cs
    public void ClearCurrentAction()
    {
        // 1. On arrête la coroutine pour éviter qu'OnApplyEffect ne s'exécute
        if (_actionRoutine != null)
        {
            StopCoroutine(_actionRoutine);
            _actionRoutine = null;
        }

        // 2. On se désabonne pour éviter les fuites de mémoire
        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction;
        }

        // 3. On libère le slot
        _currentAction = null;

        Debug.Log($"<color=orange>[Actions]</color> Action forcée à l'arrêt pour {_character.CharacterName}");
    }

    // Si le personnage est détruit, on s'assure que tout s'arrête
    private void OnDisable()
    {
        StopAllCoroutines();
        _currentAction = null;
        _actionRoutine = null;
    }
}