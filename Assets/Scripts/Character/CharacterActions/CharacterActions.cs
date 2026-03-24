using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class CharacterActions : CharacterSystem
{
    public Action<CharacterAction> OnActionStarted;
    public Action OnActionFinished;
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

        // Owner/Client -> Server Intent (Only if it's the real action, not a proxy)
        if (!IsServer && IsOwner && !(action is CharacterVisualProxyAction))
        {
            // Owner predicts visually, but doesn't apply effect safely within the action itself (or action handles IsServer check)
            // Wait, we just execute locally for prediction. The real logic executed on Server.
            // But if we execute locally, Owner's OnApplyEffect runs! We must ensure Actions check IsServer internally, 
            // OR we don't predict complex generic actions, we just wait for the Server?
            // Interaction and Combat already bypass this. Let's just predict visually.
        }

        if (IsServer && !(action is CharacterVisualProxyAction) && !action.IsReplicatedInternally)
        {
            // Server broadcasts the visual proxy to all clients (except itself)
            BroadcastActionVisualsClientRpc(action.ShouldPlayGenericActionAnimation, action.Duration);
        }

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
                if (IsServer || action is CharacterVisualProxyAction)
                    action.OnApplyEffect(); 
                // Wait, if it's NOT server and NOT proxy (meaning Owner local prediction), should we apply effect?
                // Real actions MUST check character.IsServer inside OnApplyEffect to be safe.
                else 
                    action.OnApplyEffect(); // We let it run, Actions should be refactored to check IsServer for critical data
                
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

    [Rpc(SendTo.NotServer)]
    private void BroadcastActionVisualsClientRpc(bool shouldPlayGeneric, float duration)
    {
        if (IsOwner && _currentAction != null) return; // Owner may have already predicted it

        ClearCurrentAction(); // Clear any visual desyncs

        var proxy = new CharacterVisualProxyAction(_character, duration, shouldPlayGeneric);
        ExecuteAction(proxy);
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

        OnActionFinished?.Invoke();
    }

    public void ClearCurrentAction()
    {
        if (IsServer) CancelActionVisualsClientRpc();
        ClearCurrentActionLocally();
    }

    [Rpc(SendTo.NotServer)]
    private void CancelActionVisualsClientRpc()
    {
        ClearCurrentActionLocally();
    }

    private void ClearCurrentActionLocally()
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

            OnActionFinished?.Invoke();
        }

        _currentAction = null;
    }

    // Si le personnage est détruit, on s'assure que tout s'arrête
    protected override void OnDisable()
    {
        base.OnDisable();
        StopAllCoroutines();
        _currentAction = null;
        _actionRoutine = null;
    }

    protected override void HandleIncapacitated(Character character)
    {
        ClearCurrentActionLocally();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (inCombat) ClearCurrentActionLocally();
    }
}

public class CharacterVisualProxyAction : CharacterAction
{
    private bool _shouldPlayGeneric;
    public override bool ShouldPlayGenericActionAnimation => _shouldPlayGeneric;

    public CharacterVisualProxyAction(Character character, float duration, bool shouldPlayGeneric) : base(character, duration)
    {
        _shouldPlayGeneric = shouldPlayGeneric;
    }

    public override void OnStart() { }
    
    public override void OnApplyEffect() 
    { 
        // Visual proxy does not mutate any game state
    } 
}