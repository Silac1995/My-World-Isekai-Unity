using System;
using System.Collections;
using UnityEngine;

public class CharacterActions : MonoBehaviour
{
    [SerializeField] private Character _character;

    public Action<CharacterAction> OnActionStarted;
    public Action OnActionCanceled;
    private float _actionStartTime; // Pour calculer la progression

    private CharacterAction _currentAction;
    private Coroutine _actionRoutine; // Référence pour éviter les accumulations

    public CharacterAction CurrentAction => _currentAction;

    public float GetActionProgress()
    {
        if (_currentAction == null || _currentAction.Duration <= 0) return 0f;
        float elapsed = Time.time - _actionStartTime;
        return Mathf.Clamp01(elapsed / _currentAction.Duration);
    }

    public bool ExecuteAction(CharacterAction action)
    {
        if (action == null || _currentAction != null) return false;
        if (!action.CanExecute()) return false;

        _currentAction = action;
        _actionStartTime = Time.time;
        _currentAction.OnActionFinished += CleanupAction;

        OnActionStarted?.Invoke(_currentAction);

        // 1. On lance l'initialisation de l'action
        _currentAction.OnStart();

        // 2. GESTION DU FLUX (Instantané vs Temporisé)
        if (action.Duration <= 0)
        {
            // On exécute tout de suite sans créer de Coroutine (économie de mémoire)
            try
            {
                action.OnApplyEffect();
                action.Finish();
            }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterActions] Erreur Action Instantanée: {e.Message}");
                CleanupAction();
            }
        }
        else
        {
            // On ne crée la coroutine que si nécessaire
            _actionRoutine = StartCoroutine(ActionTimerRoutine(_currentAction));
        }

        return true;
    }

    private IEnumerator ActionTimerRoutine(CharacterAction action)
    {
        if (action == null) yield break;

        // On attend la durée prévue
        if (action.Duration > 0)
        {
            yield return new WaitForSeconds(action.Duration);
        }

        // --- SÉCURITÉ ---
        // Si l'action a été annulée ou terminée entre temps (ClearCurrentAction), on arrête tout.
        if (_currentAction != action) yield break;

        try
        {
            // On applique l'effet
            action.OnApplyEffect();

            // On termine l'action (ce qui déclenchera CleanupAction via l'event)
            action.Finish();
        }
        catch (Exception e)
        {
            Debug.LogError($"[CharacterActions] Erreur durant l'exécution de l'action: {e.Message}");
            CleanupAction();
        }
    }

    private void CleanupAction()
    {
        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction;
        }

        _currentAction = null;
        _actionRoutine = null;

        // --- RÉACTIVATION DE L'AGENT ---
        // Si l'action qui vient de finir avait stoppé l'agent, on le libère ici
        if (!_character.IsPlayer() && _character.Controller?.Agent != null)
        {
            _character.Controller.Agent.isStopped = false;
        }

        OnActionCanceled?.Invoke();
    }

    // Remplace ou ajoute cette méthode dans CharacterActions.cs
    public void ClearCurrentAction()
    {
        if (_actionRoutine != null)
        {
            StopCoroutine(_actionRoutine);
            _actionRoutine = null;
        }

        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction; // Désabonnement important
            _currentAction.OnCancel(); // Permet à l'action de se désabonner (évite memory leaks)

            var animHandler = _character.CharacterVisual?.CharacterAnimator;
            if (animHandler != null)
            {
                animHandler.ResetActionTriggers();
            }

            OnActionCanceled?.Invoke();
        }

        _currentAction = null;
    }

    // Si le personnage est détruit, on s'assure que tout s'arrête
    private void OnDisable()
    {
        StopAllCoroutines();
        _currentAction = null;
        _actionRoutine = null;
    }
}