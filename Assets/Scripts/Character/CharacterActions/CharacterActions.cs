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
            // Owner predicts visually.
        }

        if (IsServer && !(action is CharacterVisualProxyAction) && !action.IsReplicatedInternally)
        {
            // Server broadcasts the visual proxy to all clients
            // MODIFICATION: Add ActionName to sync UI display
            BroadcastActionVisualsClientRpc(action.ShouldPlayGenericActionAnimation, action.Duration, action.ActionName);
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
            try
            {
                if (IsServer || action is CharacterVisualProxyAction)
                    action.OnApplyEffect(); 
                else 
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
            _actionRoutine = StartCoroutine(ActionTimerRoutine(_currentAction));
        }

        return true;
    }

    [Rpc(SendTo.NotServer)]
    private void BroadcastActionVisualsClientRpc(bool shouldPlayGeneric, float duration, string actionName)
    {
        if (IsOwner && _currentAction != null) return; // Owner may have already predicted it

        ClearCurrentAction(); // Clear any visual desyncs

        var proxy = new CharacterVisualProxyAction(_character, duration, shouldPlayGeneric, actionName);
        ExecuteAction(proxy);
    }

    private IEnumerator ActionTimerRoutine(CharacterAction action)
    {
        if (action == null) yield break;

        if (action.Duration > 0)
        {
            yield return new WaitForSeconds(action.Duration);
        }

        if (_currentAction != action) yield break;

        try
        {
            action.OnApplyEffect();
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
            _currentAction.OnActionFinished -= CleanupAction;
            _currentAction.OnCancel(); 

            var animHandler = _character.CharacterVisual?.CharacterAnimator;
            if (animHandler != null)
            {
                animHandler.ResetActionTriggers();
            }

            OnActionFinished?.Invoke();
        }

        _currentAction = null;
    }

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
    private string _proxyActionName;
    public override bool ShouldPlayGenericActionAnimation => _shouldPlayGeneric;
    public override string ActionName => _proxyActionName;

    public CharacterVisualProxyAction(Character character, float duration, bool shouldPlayGeneric, string actionName) : base(character, duration)
    {
        _shouldPlayGeneric = shouldPlayGeneric;
        _proxyActionName = actionName;
    }

    public override void OnStart() { }
    
    public override void OnApplyEffect() 
    { 
        // Visual proxy does not mutate any game state
    } 
}
