using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GoapAction_WearClothing : GoapAction
{
    public override string ActionName => "WearClothing";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isNaked", false }
    };

    public override float Cost => 1f;

    private bool _isComplete = false;
    private bool _isMoving = false;
    private bool _actionStarted = false;
    private ItemInteractable _targetInteractable;
    private Vector3 _lastTargetPos = Vector3.positiveInfinity;
    private float _lastRouteRequestTime = 0f;
    
    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_targetInteractable != null && _targetInteractable.RootGameObject != null) return true;

        List<WearableType> missingTypes = GetMissingTypes(worker);
        _targetInteractable = FindClosestWearable(worker, missingTypes);
        return _targetInteractable != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (_targetInteractable == null || _targetInteractable.RootGameObject == null || _targetInteractable.ItemInstance is not EquipmentInstance equip)
        {
            _isComplete = true;
            return;
        }

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        GameObject rootObject = _targetInteractable.RootGameObject;
        Vector3 targetPos = rootObject.transform.position;

        // 1. Déplacement vers l'objet
        bool isCloseEnough = false;
        
        if (_targetInteractable.InteractionZone != null)
        {
            // On vérifie si on est dans la zone d'interaction
            if (_targetInteractable.InteractionZone.bounds.Contains(worker.transform.position))
            {
                isCloseEnough = true;
            }
        }
        else
        {
            // Fallback (on garde l'ancien système au cas où)
            Vector3 currentPos = worker.transform.position;
            currentPos.y = 0;
            targetPos.y = 0;
            if (Vector3.Distance(currentPos, targetPos) <= 1.2f)
            {
                isCloseEnough = true;
            }
        }

        if (!isCloseEnough)
        {
            bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

            if (!_isMoving || Vector3.Distance(_lastTargetPos, targetPos) > 1f || hasPathFailed)
            {
                movement.SetDestination(rootObject.transform.position);
                _lastTargetPos = targetPos;
                _lastRouteRequestTime = UnityEngine.Time.time;
                _isMoving = true;
            }
            return;
        }

        // 2. Arrivé à l'objet
        if (_isMoving)
        {
            movement.Stop();
            _isMoving = false;
            _lastTargetPos = Vector3.positiveInfinity;
        }

        // 3. Collecter et équiper (Attendre que l'action se termine)
        if (!_actionStarted)
        {
            if (_targetInteractable.TryCollect())
            {
                CharacterEquipAction equipAction = new CharacterEquipAction(worker, equip);
                bool success = worker.CharacterActions.ExecuteAction(equipAction);
                if (success)
                {
                    _actionStarted = true;
                    // On détruit l'objet physique au sol dès qu'on commence l'équipement pour éviter les doublons
                    if (rootObject != null) UnityEngine.Object.Destroy(rootObject);
                }
                else
                {
                    // Action occupée, on échoue gracieusement
                    _isComplete = true;
                    return;
                }
            }
            else 
            {
                _isComplete = true; // Déjà pris par qqun d'autre
                return;
            }
        }
        else
        {
            // Attente de la fin de l'action CharacterEquipAction (0.8s)
            if (!(worker.CharacterActions.CurrentAction is CharacterEquipAction))
            {
                _isComplete = true;
            }
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _actionStarted = false;
        _targetInteractable = null;
        worker.CharacterMovement?.Stop();
    }

    private List<WearableType> GetMissingTypes(Character worker)
    {
        List<WearableType> missingTypes = new List<WearableType>();
        if (worker.CharacterEquipment.IsGroinExposed()) missingTypes.Add(WearableType.Pants);
        if (worker.CharacterEquipment.IsChestExposed()) missingTypes.Add(WearableType.Armor);
        return missingTypes;
    }

    private ItemInteractable FindClosestWearable(Character worker, List<WearableType> typesToFind)
    {
        var awareness = worker.CharacterAwareness;
        if (awareness == null) return null;

        return awareness.GetVisibleInteractables<ItemInteractable>()
            .Where(item => {
                if (item.ItemInstance is WearableInstance w)
                    return typesToFind.Contains(((WearableSO)w.ItemSO).WearableType);
                return false;
            })
            .OrderBy(item => Vector3.Distance(worker.transform.position, item.transform.position))
            .FirstOrDefault();
    }
}
