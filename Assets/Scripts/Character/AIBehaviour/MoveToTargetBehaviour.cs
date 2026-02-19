using System;
using UnityEngine;

public class MoveToTargetBehaviour : IAIBehaviour
{
    private NPCController _controller;
    private GameObject _targetGameObject;
    private Vector3 _targetPosition;
    private float _stoppingDistance;
    private Action _onArrived;
    private bool _isFinished = false;
    public bool IsFinished => _isFinished;

    public MoveToTargetBehaviour(NPCController controller, GameObject target, float stopDist, Action onArrived)
    {
        _controller = controller;
        _targetGameObject = target;
        _stoppingDistance = stopDist;
        _onArrived = onArrived;
    }

    public MoveToTargetBehaviour(NPCController controller, Vector3 pos, float stopDist, Action onArrived)
    {
        _controller = controller;
        _targetPosition = pos;
        _targetGameObject = null;
        _stoppingDistance = stopDist;
        _onArrived = onArrived;
    }

    public void Act(Character character)
    {
        if (_targetGameObject == null && _targetPosition == Vector3.zero)
        {
            _isFinished = true;
            return;
        }

        var detector = _controller.GetComponent<CharacterInteractionDetector>();
        var movement = character.CharacterMovement;
        if (movement == null) return;

        if (_targetGameObject != null)
        {
            var targetInteractable = _targetGameObject.GetComponentInChildren<InteractableObject>();

            if (detector != null && targetInteractable != null)
            {
                if (detector.IsOverlapping(targetInteractable))
                {
                    StopAndArrive(character);
                    return;
                }
            }

            UpdateAgentDestination(movement, _targetGameObject.transform.position);
        }
        else
        {
            float dist = Vector3.Distance(character.transform.position, _targetPosition);
            if (dist <= _stoppingDistance)
            {
                StopAndArrive(character);
                return;
            }
            UpdateAgentDestination(movement, _targetPosition);
        }
    }

    private void UpdateAgentDestination(CharacterMovement movement, Vector3 targetPos)
    {
        // On ne recalcule le chemin que si la cible a bougé significativment
        if (Vector3.Distance(movement.Destination, targetPos) > 0.1f)
        {
            movement.SetDestination(targetPos);
        }
    }

    private void StopAndArrive(Character character)
    {
        character.CharacterMovement?.Stop();
        _onArrived?.Invoke();
        _isFinished = true;
    }

    public void Exit(Character character)
    {
        character.CharacterMovement?.ResetPath();
    }
    
    public void Terminate() => _isFinished = true;
}
