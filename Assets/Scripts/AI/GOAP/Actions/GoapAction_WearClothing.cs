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
    private bool _actionStarted = false;
    private ItemInteractable _targetInteractable;
    private float _cooldownEndTime = 0f;
    private Vector3 _targetPos;
    
    private WearState _currentState = WearState.FindingItem;

    private enum WearState
    {
        FindingItem,
        MovingToItem,
        Equipping
    }

    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (UnityEngine.Time.time < _cooldownEndTime) return false;

        // On vérifie d'abord si on a un objet cible actuellement valide
        if (_targetInteractable != null && _targetInteractable.RootGameObject != null) return true;

        List<WearableType> missingTypes = GetMissingTypes(worker);
        _targetInteractable = FindClosestWearable(worker, missingTypes);
        return _targetInteractable != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        var movement = worker.CharacterMovement;
        if (movement == null)
        {
            _isComplete = true;
            return;
        }

        switch (_currentState)
        {
            case WearState.FindingItem:
                if (_targetInteractable == null || _targetInteractable.RootGameObject == null)
                {
                    List<WearableType> missingTypes = GetMissingTypes(worker);
                    _targetInteractable = FindClosestWearable(worker, missingTypes);
                }

                if (_targetInteractable != null)
                {
                    _currentState = WearState.MovingToItem;
                }
                else
                {
                    _isComplete = true; 
                }
                break;

            case WearState.MovingToItem:
                if (_targetInteractable == null || _targetInteractable.RootGameObject == null)
                {
                    // L'item a été détruit par quelqu'un d'autre
                    Debug.Log($"<color=teal>[WearClothing]</color> {worker.CharacterName} a perdu la course. Vêtement pris par un autre. Cooldown...");
                    _cooldownEndTime = UnityEngine.Time.time + 1.5f;
                    _isComplete = true;
                    return;
                }

                GameObject rootObject = _targetInteractable.RootGameObject;
                Collider targetCol = _targetInteractable.InteractionZone;
                
                if (targetCol != null)
                {
                    _targetPos = targetCol.bounds.ClosestPoint(worker.transform.position);
                }
                else
                {
                    _targetPos = rootObject.transform.position;
                }

                HandleMovementTo(worker, _targetPos, out bool arrived, targetCol);

                // Anti-stuck : S'il y a trop de monde sur l'objet et qu'on ne peut pas l'atteindre
                float distToTarget = Vector3.Distance(new Vector3(worker.transform.position.x, 0, worker.transform.position.z), new Vector3(_targetPos.x, 0, _targetPos.z));
                if (!arrived && distToTarget <= 2f && movement.GetVelocity().sqrMagnitude < 0.1f)
                {
                    // On force l'arrivée si on est bloqué par la foule juste devant
                    arrived = true;
                    movement.ResetPath();
                }

                if (arrived)
                {
                    _currentState = WearState.Equipping;
                    _actionStarted = false;
                }
                break;

            case WearState.Equipping:
                if (_targetInteractable == null || _targetInteractable.RootGameObject == null || _targetInteractable.ItemInstance is not EquipmentInstance equip)
                {
                    Debug.Log($"<color=teal>[WearClothing]</color> {worker.CharacterName} a perdu la course. Vêtement pris par un autre. Cooldown...");
                    _cooldownEndTime = UnityEngine.Time.time + 1.5f;
                    _isComplete = true;
                    return;
                }

                if (!_actionStarted)
                {
                    // FIX: Wait for the character to finish any current action before trying to equip
                    if (worker.CharacterActions.CurrentAction != null)
                    {
                        return; // Wait next tick
                    }

                    if (_targetInteractable.TryCollect())
                    {
                        CharacterEquipAction equipAction = new CharacterEquipAction(worker, equip);
                        bool success = worker.CharacterActions.ExecuteAction(equipAction);
                        
                        if (success)
                        {
                            _actionStarted = true;
                            // On détruit l'objet physique au sol dès qu'on commence l'équipement pour éviter les doublons
                            var rootToDestroy = _targetInteractable.RootGameObject;
                            if (rootToDestroy != null) UnityEngine.Object.Destroy(rootToDestroy);
                        }
                        else
                        {
                            // FIX: If ExecuteAction fails, free the item so others can pick it up
                            _targetInteractable.CancelCollect();
                            _isComplete = true;
                            return;
                        }
                    }
                    else 
                    {
                        Debug.Log($"<color=teal>[WearClothing]</color> {worker.CharacterName} est arrivé mais l'objet n'est plus collectable. Cooldown...");
                        _cooldownEndTime = UnityEngine.Time.time + 1.5f;
                        _isComplete = true; 
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
                break;
        }
    }

    private void HandleMovementTo(Character worker, Vector3 targetPos, out bool arrived, Collider targetCollider = null)
    {
        arrived = false;
        var movement = worker.CharacterMovement;

        // Si on a un collider cible, on vérifie direct l'intersection des bounds
        if (targetCollider != null)
        {
            var workerCol = worker.GetComponent<Collider>();
            if (workerCol != null && targetCollider.bounds.Intersects(workerCol.bounds))
            {
                movement.ResetPath();
                arrived = true;
                return;
            }
        }

        float distance = Vector3.Distance(new Vector3(worker.transform.position.x, 0, worker.transform.position.z), new Vector3(targetPos.x, 0, targetPos.z));

        if (movement.PathPending) return;

        if (!movement.HasPath || movement.RemainingDistance <= movement.StoppingDistance + 0.5f)
        {
            if (distance > movement.StoppingDistance + 0.5f)
            {
                movement.SetDestination(targetPos);
            }
            else
            {
                movement.ResetPath();
                arrived = true;
            }
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _actionStarted = false;
        _currentState = WearState.FindingItem;
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
                if (item == null || item.RootGameObject == null) return false;
                if (item.WorldItem != null && item.WorldItem.IsBeingCarried) return false;

                if (item.ItemInstance is WearableInstance w)
                    return typesToFind.Contains(((WearableSO)w.ItemSO).WearableType);
                return false;
            })
            .OrderBy(item => Vector3.Distance(worker.transform.position, item.transform.position))
            .FirstOrDefault();
    }
}
