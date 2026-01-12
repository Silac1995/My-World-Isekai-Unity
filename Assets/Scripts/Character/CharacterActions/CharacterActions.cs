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

    public void ExecuteAction(CharacterAction action)
    {
        if (action == null || _currentAction != null) return;
        if (!action.CanExecute()) return;

        _currentAction = action;
        _actionStartTime = Time.time; // On enregistre le début
        _currentAction.OnActionFinished += CleanupAction;

        OnActionStarted?.Invoke(_currentAction); // On prévient l'UI

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
        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction;
        }

        _currentAction = null;
        _actionRoutine = null;

        // Ajoute ceci pour que l'UI sache qu'il faut se cacher même si c'est une réussite
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
            // --- LA CORRECTION EST ICI ---
            // On demande à l'animator de supprimer le trigger s'il n'a pas encore été consommé
            var animator = _character.CharacterVisual?.CharacterAnimator?.Animator;
            if (animator != null)
            {
                animator.ResetTrigger("Trigger_pickUpItem");
                // Si tu as d'autres actions, tu peux aussi reset leurs triggers ici
                // animator.ResetTrigger("Trigger_Sit"); 
                OnActionCanceled?.Invoke(); // On prévient l'UI pour cacher la barre
            }

            _currentAction.OnActionFinished -= CleanupAction;
        }

        _currentAction = null;
        Debug.Log("<color=orange>[Actions]</color> Action et Animation annulées.");
    }

    // Si le personnage est détruit, on s'assure que tout s'arrête
    private void OnDisable()
    {
        StopAllCoroutines();
        _currentAction = null;
        _actionRoutine = null;
    }
}