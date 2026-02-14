using System;
using UnityEngine;

public class MoveToTargetBehaviour : IAIBehaviour
{
    private NPCController _controller;
    private GameObject _targetGameObject; // Changé de Transform à GameObject
    private Vector3 _targetPosition;
    private float _stoppingDistance;
    private Action _onArrived;
    private bool _isFinished = false;
    public bool IsFinished => _isFinished;

    // Constructeur 1 : Cible physique (GameObject)
    public MoveToTargetBehaviour(NPCController controller, GameObject target, float stopDist, Action onArrived)
    {
        _controller = controller;
        _targetGameObject = target;
        _stoppingDistance = stopDist;
        _onArrived = onArrived;
    }

    // Constructeur 2 : Destination fixe (Vector3)
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
        // 1. Si on a un GameObject cible, on vérifie s'il existe toujours
        if (_targetGameObject == null && _targetPosition == Vector3.zero)
        {
            _controller.PopBehaviour();
            return;
        }

        // On récupère le detector via le controller
        var detector = _controller.GetComponent<CharacterInteractionDetector>();

        if (_targetGameObject != null)
        {
            var targetInteractable = _targetGameObject.GetComponentInChildren<InteractableObject>();

            // ON UTILISE TES MÉTHODES ICI
            if (detector != null && targetInteractable != null)
            {
                // IsOverlapping utilise bounds.Intersects, c'est parfait et précis.
                if (detector.IsOverlapping(targetInteractable))
                {
                    StopAndArrive(character);
                    return;
                }
            }

            UpdateAgentDestination(_targetGameObject.transform.position);
        }
        // --- LOGIQUE POUR POSITION FIXE ---
        else
        {
            float dist = Vector3.Distance(character.transform.position, _targetPosition);
            if (dist <= _stoppingDistance)
            {
                StopAndArrive(character);
                return;
            }
            UpdateAgentDestination(_targetPosition);
        }
    }

    private void UpdateAgentDestination(Vector3 targetPos)
    {
        if (_controller.Agent.isOnNavMesh)
        {
            // On ne recalcule le chemin que si la cible a bougé significativement
            if (Vector3.Distance(_controller.Agent.destination, targetPos) > 0.1f)
            {
                _controller.Agent.SetDestination(targetPos);
            }
        }
    }

    private void StopAndArrive(Character character)
    {
        Debug.Log($"<color=green>[IA]</color> Arrivée confirmée.");
        if (_controller.Agent.isOnNavMesh)
        {
            _controller.Agent.ResetPath();
            _controller.Agent.velocity = Vector3.zero;
        }

        _onArrived?.Invoke();

        // AU LIEU DE PopBehaviour directement, on utilise le flag :
        _isFinished = true;
    }

    public void Exit(Character character)
    {
        // On nettoie juste le chemin pour ne pas que le NPC 
        // "glisse" vers l'ancienne destination au démarrage du prochain behaviour.
        if (_controller.Agent != null && _controller.Agent.isOnNavMesh)
        {
            _controller.Agent.ResetPath();
        }
    }
    public void Terminate() => _isFinished = true;
}