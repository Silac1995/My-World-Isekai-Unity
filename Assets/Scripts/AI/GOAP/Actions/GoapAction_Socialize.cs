using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class GoapAction_Socialize : GoapAction
{
    public override string ActionName => "Socialize";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>();

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "isLonely", false }
    };

    public override float Cost => 2f;

    private bool _isComplete = false;
    private bool _isMoving = false;
    private Character _target;
    private Vector3 _lastTargetPos = Vector3.positiveInfinity;
    private float _lastRouteRequestTime = 0f;
    private bool _hasStartedInteraction = false;
    
    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        if (_isComplete) return false;
        if (_target != null && _target.IsAlive() && _target.IsFree()) return true;

        _target = FindBestSocialPartner(worker);
        return _target != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (worker.CharacterInteraction.IsInteractionProcessActive)
        {
            return; // on attend la fin de l'interaction
        }

        if (_hasStartedInteraction)
        {
            // L'interaction est terminée !
            var needSocial = worker.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
            if (needSocial != null)
            {
                needSocial.IncreaseValue(50f); // Satisfait grandement le besoin
            }
            _isComplete = true;
            return;
        }

        if (_target == null || !_target.IsAlive() || !_target.IsFree())
        {
            // Impossible de trouver une cible ou la cible n'est plus libre
            var needSocial = worker.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
            if (needSocial != null) needSocial.SetCooldown(); // On attend avant de réessayer
            
            _isComplete = true;
            return;
        }

        var movement = worker.CharacterMovement;
        if (movement == null) 
        {
            _isComplete = true;
            return;
        }

        Vector3 targetPos = _target.transform.position;
        var interactable = _target.GetComponentInChildren<CharacterInteractable>();
        if (interactable != null && interactable.InteractionZone != null)
        {
            targetPos = interactable.InteractionZone.bounds.ClosestPoint(worker.transform.position);
        }

        Vector3 currentPos = worker.transform.position;
        currentPos.y = 0;
        targetPos.y = 0;
        
        float distance = Vector3.Distance(currentPos, targetPos);

        // 1. Déplacement vers la cible (Socialize trigger distance is roughly 1f here because we already take the edge)
        if (distance > 1.5f)
        {
            bool hasPathFailed = (UnityEngine.Time.unscaledTime - _lastRouteRequestTime > 0.2f) && (movement.PathStatus == UnityEngine.AI.NavMeshPathStatus.PathInvalid || (!movement.HasPath && !movement.PathPending));

            if (!_isMoving || Vector3.Distance(_lastTargetPos, targetPos) > 1f || hasPathFailed)
            {
                movement.SetDestination(targetPos);
                _lastTargetPos = targetPos;
                _lastRouteRequestTime = UnityEngine.Time.unscaledTime;
                _isMoving = true;
            }
            return;
        }

        // 2. Arrivé près de la cible
        if (_isMoving)
        {
            movement.Stop();
            _isMoving = false;
            _lastTargetPos = Vector3.positiveInfinity;
        }

        // 3. Déclencher l'interaction
        bool success = worker.CharacterInteraction.StartInteractionWith(_target, onPositioned: () => 
        {
            // Logique éventuelle une fois positionné
        });

        if (success)
        {
            _hasStartedInteraction = true;
        }
        else
        {
            // L'interaction a échoué (ex: la cible vient juste de commencer à parler avec un autre)
            var needSocial = worker.CharacterNeeds?.AllNeeds.OfType<NeedSocial>().FirstOrDefault();
            if (needSocial != null) needSocial.SetCooldown();
            _isComplete = true;
        }
    }

    public override void Exit(Character worker)
    {
        _isComplete = false;
        _isMoving = false;
        _hasStartedInteraction = false;
        _target = null;
        worker.CharacterMovement?.Stop();
    }

    private Character FindBestSocialPartner(Character worker)
    {
        var awareness = worker.GetComponentInChildren<CharacterAwareness>();
        if (awareness == null) return null;

        var nearbyPartners = awareness.GetVisibleInteractables<CharacterInteractable>()
            .Select(interactable => interactable.Character)
            .Where(c => c != null && c.IsAlive() && c.IsFree() && c != worker
                     && c.CharacterSchedule?.CurrentActivity != ScheduleActivity.Work)
            .ToList();

        if (!nearbyPartners.Any()) return null;

        var knownPartners = nearbyPartners
            .Where(c => worker.CharacterRelation != null && worker.CharacterRelation.GetRelationshipWith(c)?.RelationValue > 0)
            .OrderBy(c => Vector3.Distance(worker.transform.position, c.transform.position))
            .ToList();

        var otherPartners = nearbyPartners
            .Except(knownPartners)
            .OrderBy(c => Vector3.Distance(worker.transform.position, c.transform.position))
            .ToList();

        bool prioritizeKnown = Random.value < 0.8f;

        if (prioritizeKnown)
        {
            if (knownPartners.Any()) return knownPartners[0];
            if (otherPartners.Any()) return otherPartners[0];
        }
        else
        {
            if (otherPartners.Any()) return otherPartners[0];
            if (knownPartners.Any()) return knownPartners[0];
        }

        return null;
    }
}
