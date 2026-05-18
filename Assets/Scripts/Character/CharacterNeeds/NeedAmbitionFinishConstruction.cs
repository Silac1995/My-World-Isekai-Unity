using System.Collections.Generic;
using UnityEngine;
using MWI.Ambition;
using MWI.WorldSystem;

/// <summary>
/// "I have an active ambition step that needs a building I placed to finish
/// construction." Activates when:
/// <list type="bullet">
/// <item>The character has a server-side <see cref="CharacterAmbition.Current"/>
/// whose <c>CurrentStepQuest</c> is an <see cref="AmbitionQuest"/></item>
/// <item>That step contains at least one <see cref="Task_FinishConstruction"/></item>
/// <item>At least one <see cref="Building"/> in <see cref="BuildingManager.allBuildings"/>
/// matches that task's <c>TargetBlueprint</c>, was placed by this actor
/// (<c>PlacedByCharacterId == actor.CharacterId</c>), and is still
/// <see cref="Building.IsUnderConstruction"/>.</item>
/// </list>
///
/// <para>Goal: <c>"ambitionBuildingFinalized" = true</c>. Action chain:
/// <see cref="GoapAction_FulfillAmbitionConstruction"/> (composite — runs the
/// scan-awareness → harvest → pickup → carry → drop → consume state machine).</para>
///
/// <para>Urgency is moderately high (above the BASE_URGENCY=60 of NeedJob) so the
/// founder commits to building their capital before chasing day jobs. Hunger /
/// Sleep / Combat still preempt via the Behaviour Tree, not via GOAP urgency.</para>
///
/// <para>Lifecycle: POCO created by <see cref="CharacterNeeds"/> in the same Awake
/// pass as the other needs. No network subscription required — IsActive is a pure
/// read from <see cref="CharacterAmbition"/> + <see cref="BuildingManager"/>, both
/// of which already work on the server-only side. Clients keep the Need but it
/// returns IsActive=false (GOAP doesn't run on clients anyway).</para>
/// </summary>
public class NeedAmbitionFinishConstruction : CharacterNeed
{
    /// <summary>Higher than NeedJob (60) so founding commitments win the planner pick.</summary>
    private const float BASE_URGENCY = 75f;

    public NeedAmbitionFinishConstruction(Character character) : base(character)
    {
    }

    public override bool IsActive()
    {
        if (_character == null) return false;
        if (_character.Controller is PlayerController) return false; // GOAP is NPC-only.

        var ambition = _character.CharacterAmbition;
        if (ambition == null || !ambition.HasActive) return false;
        if (!(ambition.Current?.CurrentStepQuest is AmbitionQuest aq)) return false;

        var tasks = aq.Tasks;
        if (tasks == null) return false;

        var bm = BuildingManager.Instance;
        if (bm == null) return false;

        string actorId = _character.CharacterId;
        if (string.IsNullOrEmpty(actorId)) return false;

        // Walk tasks for any Task_FinishConstruction whose TargetBlueprint matches a
        // still-under-construction building placed by this actor. First match → active.
        for (int t = 0; t < tasks.Count; t++)
        {
            if (!(tasks[t] is Task_FinishConstruction finish)) continue;
            if (finish.TargetBlueprint == null) continue;
            for (int i = 0; i < bm.allBuildings.Count; i++)
            {
                var b = bm.allBuildings[i];
                if (b == null) continue;
                if (!b.IsUnderConstruction) continue;
                if (b.Blueprint != finish.TargetBlueprint) continue;
                if (b.PlacedByCharacterId.Value.ToString() != actorId) continue;
                return true;
            }
        }
        return false;
    }

    public override float GetUrgency()
    {
        return IsActive() ? BASE_URGENCY : 0f;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal(
            "FinalizeAmbitionBuilding",
            new Dictionary<string, bool> { { "ambitionBuildingFinalized", true } },
            (int)GetUrgency());
    }

    public override List<GoapAction> GetGoapActions()
    {
        var actions = new List<GoapAction>();
        // Single composite action — see GoapAction_FulfillAmbitionConstruction for the
        // 5-step internal state machine. Splitting into atomic GoapActions (Harvest /
        // Pickup / GoToZone / Drop / Finalize as separate actions) is a future
        // refactor; the composite is enough to render visibly in CharacterGoapController
        // inspectors and to reuse the canonical CharacterAction atoms underneath.
        actions.Add(new GoapAction_FulfillAmbitionConstruction());
        return actions;
    }
}
