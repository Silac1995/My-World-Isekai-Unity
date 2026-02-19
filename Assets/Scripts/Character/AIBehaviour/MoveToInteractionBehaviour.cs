using UnityEngine;
using System;

public class MoveToInteractionBehaviour : IAIBehaviour
{
    private CharacterGameController _controller;
    private Character _targetCharacter;
    private bool _isFinished = false;
    private Action _onArrived;
    
    // --- TIMEOUT ---
    private float _timeoutTimer = 0f;
    private const float TIMEOUT_DURATION = 5f;

    public bool IsFinished => _isFinished;

    public void Terminate() => _isFinished = true;

    public MoveToInteractionBehaviour(CharacterGameController controller, Character target, Action onArrived = null)
    {
        _controller = controller;
        _targetCharacter = target;
        _onArrived = onArrived;
    }

    public void Act(Character self)
    {
        if (_targetCharacter == null || !self.CharacterInteraction.IsInteracting)
        {
            _isFinished = true;
            return;
        }

        // Gestion du Timeout
        _timeoutTimer += Time.deltaTime;
        if (_timeoutTimer > TIMEOUT_DURATION)
        {
            Debug.LogWarning($"<color=orange>[Interaction]</color> Timeout de positionnement pour {self.CharacterName} vers {_targetCharacter.CharacterName}. ABORT.");
            self.CharacterInteraction.EndInteraction();
            _isFinished = true;
            return;
        }

        var movement = self.CharacterMovement;
        if (movement == null) return;

        Vector3 targetPos = _targetCharacter.transform.position;
        float xOffset = self.transform.position.x > targetPos.x ? 5f : -5f;
        Vector3 desiredPos = new Vector3(targetPos.x + xOffset, self.transform.position.y, targetPos.z);

        float distDelta = Vector3.Distance(new Vector3(self.transform.position.x, 0, self.transform.position.z), 
                                           new Vector3(desiredPos.x, 0, desiredPos.z));

        if (distDelta < 0.25f)
        {
            FaceTarget(self);

            if (_targetCharacter.Controller != null)
            {
                Vector3 targetToInitiator = self.transform.position - _targetCharacter.transform.position;
                _targetCharacter.CharacterVisual?.UpdateFlip(targetToInitiator);
            }

            self.CharacterInteraction.SetPositioned(true);
            movement.Stop();
            
            _isFinished = true; 
            _onArrived?.Invoke();
            return; 
        }

        self.CharacterInteraction.SetPositioned(false);
        movement.Resume();
        movement.SetDestination(desiredPos);
    }

    private void FaceTarget(Character self)
    {
        Vector3 direction = _targetCharacter.transform.position - self.transform.position;
        self.CharacterVisual?.UpdateFlip(direction);
    }

    public void Exit(Character self)
    {
        self.CharacterMovement?.ResetPath();
    }
}
