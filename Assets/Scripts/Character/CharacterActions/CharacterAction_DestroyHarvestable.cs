using UnityEngine;

/// <summary>
/// Generic destruction of any Harvestable (apple tree, wild forest tree, etc.).
/// Queued by UI_InteractionMenu when the player picks the "destroy" option.
/// NPC GOAP/BT can construct it directly without UI.
/// </summary>
public class CharacterAction_DestroyHarvestable : CharacterAction
{
    private readonly Harvestable _target;

    public CharacterAction_DestroyHarvestable(Character actor, Harvestable target)
        : base(actor, target != null ? target.DestructionDuration : 3f)
    {
        _target = target;
    }

    public override string ActionName => "Destroy";

    public override bool CanExecute()
    {
        if (_target == null) return false;
        var held = Harvestable.GetHeldItemSO(character);
        if (!_target.CanDestroyWith(held))
        {
            Debug.LogWarning($"<color=orange>[Destroy Action]</color> {character.CharacterName} cannot destroy {_target.gameObject.name} — wrong tool.");
            return false;
        }

        // Range check — same fallback logic as CharacterHarvestAction.
        if (_target.InteractionZone != null)
        {
            if (!_target.InteractionZone.bounds.Contains(character.transform.position))
            {
                float dist = Vector3.Distance(character.transform.position, _target.InteractionZone.bounds.ClosestPoint(character.transform.position));
                if (dist > 2.5f)
                {
                    Debug.LogWarning($"<color=orange>[Destroy Action]</color> {character.CharacterName} too far from {_target.gameObject.name} (Dist: {dist}).");
                    return false;
                }
            }
        }
        else
        {
            float dist = Vector3.Distance(character.transform.position, _target.transform.position);
            if (dist > 3f)
            {
                Debug.LogWarning($"<color=orange>[Destroy Action]</color> {character.CharacterName} too far from {_target.gameObject.name}.");
                return false;
            }
        }

        return true;
    }

    public override void OnStart()
    {
        Debug.Log($"<color=cyan>[Destroy Action]</color> {character.CharacterName} starts destroying {_target.gameObject.name}...");
    }

    public override void OnApplyEffect()
    {
        if (_target == null) return;

        var actions = character.CharacterActions;
        if (actions == null) return;

        // Networked clients route to the server; host/NPC/offline runs directly. Both paths go
        // through ApplyDestroyOnServer so the PickupLooseItemTask registration for each spawned
        // WorldItem fires identically — without it the harvester's planner has no looseItemExists
        // trigger after the chop and the wood sits orphaned on the ground.
        bool isNetworkedClient = actions.IsSpawned && !actions.IsServer;
        if (isNetworkedClient)
        {
            actions.RequestDestroyHarvestableServerRpc(_target.transform.position);
        }
        else
        {
            actions.ApplyDestroyOnServer(_target);
        }
    }
}
