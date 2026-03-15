using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GoapAction_GoShopping : GoapAction
{
    public override string ActionName => "GoShopping";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "shoppingDone", true }
    };

    public override float Cost => 2f;

    private ItemSO _desiredItem;
    private bool _isComplete = false;
    private bool _isMoving = false;
    private bool _hasJoinedQueue = false;
    private bool _wasInteracting = false;
    private ShopBuilding _shop;
    private Vector3 _lastTargetPos = Vector3.positiveInfinity;
    private float _lastRouteRequestTime = 0f;
    
    public override bool IsComplete => _isComplete;

    public GoapAction_GoShopping(ItemSO desiredItem)
    {
        _desiredItem = desiredItem;
    }

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_shop != null) return true;

        _shop = FindShop();
        return _shop != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (_shop == null)
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

        if (!_hasJoinedQueue)
        {
            Vector3 targetPos = _shop.transform.position;
            Vector3 currentPos = worker.transform.position;
            currentPos.y = 0; targetPos.y = 0;
            
            float distance = Vector3.Distance(currentPos, targetPos);

            if (distance > 3f)
            {
                bool hasPathFailed = (UnityEngine.Time.time - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

                if (!_isMoving || Vector3.Distance(_lastTargetPos, targetPos) > 1f || hasPathFailed)
                {
                    movement.SetDestination(_shop.transform.position);
                    _lastTargetPos = targetPos;
                    _lastRouteRequestTime = UnityEngine.Time.time;
                    _isMoving = true;
                }
                return;
            }

            if (_isMoving)
            {
                movement.Stop();
                _isMoving = false;
                _lastTargetPos = Vector3.positiveInfinity;
            }

            if (!worker.CharacterInteraction.IsInteracting)
            {
                _shop.JoinQueue(worker);
                _hasJoinedQueue = true;
            }
        }
        else
        {
            // Client is in queue or interacting
            if (worker.CharacterInteraction.IsInteracting)
            {
                _wasInteracting = true;
                return;
            }

            if (_wasInteracting)
            {
                // Interaction just finished
                _isComplete = true;
                return;
            }

            // Fallback: If there is no vendor assigned/working, maybe leave?
            // JobVendor vendor = _shop.GetVendor();
            // if (vendor == null) _isComplete = true;
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _wasInteracting = false;
        if (_hasJoinedQueue && _shop != null)
        {
            // Optionally could leave queue here if needed, but shop probably clears it
        }
        _hasJoinedQueue = false;
        _shop = null;
        worker.CharacterMovement?.Stop();
    }

    private ShopBuilding FindShop()
    {
        if (BuildingManager.Instance == null) return null;
        return BuildingManager.Instance.allBuildings
            .OfType<ShopBuilding>()
            .FirstOrDefault(s => s.ItemsToSell.Contains(_desiredItem) && s.HasItemInStock(_desiredItem));
    }
}
