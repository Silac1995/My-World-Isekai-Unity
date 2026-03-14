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
    private bool _hasStartedMoving = false;
    
    public override bool IsComplete => _isComplete;

    public override bool IsValid(Character worker)
    {
        return FindBestSocialPartner(worker) != null;
    }

    public override void Execute(Character worker)
    {
        if (_isComplete) return;

        if (worker.CharacterInteraction.IsInteracting)
        {
            return;
        }

        NPCController npc = worker.Controller as NPCController;
        if (npc == null)
        {
            _isComplete = true;
            return;
        }

        if (_hasStartedMoving)
        {
            if (!(npc.CurrentBehaviour is MoveToTargetBehaviour))
            {
                _isComplete = true; 
            }
            return;
        }

        Character target = FindBestSocialPartner(worker);
        if (target != null)
        {
            _hasStartedMoving = true;
            npc.PushBehaviour(new MoveToTargetBehaviour(npc, target.gameObject, 7f, () =>
            {
                if (target == null || !target.IsAlive())
                {
                    return;
                }

                worker.CharacterInteraction.StartInteractionWith(target, onPositioned: () => 
                {
                    // Optionally logic here on positioned 
                });
            }));
        }
        else
        {
            _isComplete = true;
        }
    }

    private Character FindBestSocialPartner(Character worker)
    {
        var awareness = worker.GetComponentInChildren<CharacterAwareness>();
        if (awareness == null) return null;

        var nearbyPartners = awareness.GetVisibleInteractables<CharacterInteractable>()
            .Select(interactable => interactable.Character)
            .Where(c => c != null && c.IsAlive() && c.IsFree() && c != worker
                     && !(c.Controller is NPCController npc && npc.CurrentBehaviour != null && npc.CurrentBehaviour.GetType().Name == "WorkBehaviour"))
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
