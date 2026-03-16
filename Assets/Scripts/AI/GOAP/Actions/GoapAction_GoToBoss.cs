using System.Collections.Generic;
using UnityEngine;
using MWI.AI;

/// <summary>
/// Action GOAP : Se rendre auprès du patron d'un bâtiment pour une interaction.
/// </summary>
public class GoapAction_GoToBoss : GoapAction_MoveToTarget
{
    public override string ActionName => "GoToBoss";

    public override Dictionary<string, bool> Preconditions => new Dictionary<string, bool>
    {
        { "knowsVacantJob", true },
        { "atBossLocation", false }
    };

    public override Dictionary<string, bool> Effects => new Dictionary<string, bool>
    {
        { "atBossLocation", true }
    };

    private Character _boss;

    public GoapAction_GoToBoss(Character boss)
    {
        _boss = boss;
    }

    public override bool IsValid(Character worker)
    {
        return _boss != null && _boss.IsAlive();
    }

    protected override Collider GetTargetCollider(Character worker)
    {
        if (_boss == null) return null;
        return _boss.Collider;
    }

    protected override Vector3 GetDestinationPoint(Character worker)
    {
        if (_boss == null) return worker.transform.position;
        return _boss.transform.position;
    }
}
