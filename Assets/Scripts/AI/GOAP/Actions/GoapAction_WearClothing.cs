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
    private ItemInteractable _targetInteractable;
    
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

        // On ignore l'axe Y pour la distance
        Vector3 currentPos = worker.transform.position;
        currentPos.y = 0;
        targetPos.y = 0;
        
        float distance = Vector3.Distance(currentPos, targetPos);

        // 1. Déplacement vers l'objet
        if (distance > 1.2f)
        {
            if (!_isMoving || movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending))
            {
                movement.SetDestination(rootObject.transform.position);
                _isMoving = true;
            }
            return;
        }

        // 2. Arrivé à l'objet
        if (_isMoving)
        {
            movement.Stop();
            _isMoving = false;
        }

        // 3. Collecter et équiper
        if (_targetInteractable.TryCollect())
        {
            CharacterEquipAction equipAction = new CharacterEquipAction(worker, equip);
            worker.CharacterActions.ExecuteAction(equipAction);
            
            if (rootObject != null) UnityEngine.Object.Destroy(rootObject);
        }

        _isComplete = true;
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _targetInteractable = null;
        worker.CharacterMovement?.Resume();
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
