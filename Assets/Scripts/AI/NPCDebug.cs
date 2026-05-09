/// <summary>
/// Central on/off switches for NPC debug logging.
///
/// These guards exist because every BT tick (0.1s) can visit many log sites per NPC — GOAP
/// replans, job-action reassignments, locate-target searches, path probes — and with even a
/// handful of NPCs the Unity console rendering cost on Windows grows fast enough to
/// progressively stall the editor. The defaults are OFF. Flip them while actively diagnosing.
///
/// Placement: in code, use <c>if (NPCDebug.VerbosePlanning) Debug.Log(...);</c>. Do NOT put a
/// bare Debug.Log inside any method reachable from `NPCBehaviourTree.Update` / `Job.Execute`
/// / `GoapAction.Execute` / movement FixedUpdate.
/// </summary>
public static class NPCDebug
{
    /// <summary>GOAP planning (GoapPlanner backward search, CharacterGoapController replans).</summary>
    public static bool VerbosePlanning = false;

    /// <summary>Per-tick job logs (JobLogisticsManager, JobTransporter, JobHarvester, JobBlacksmith).</summary>
    public static bool VerboseJobs = false;

    /// <summary>GoapAction.Execute per-tick logs (LocateItem, HarvestResources, DepositResources, etc.).</summary>
    public static bool VerboseActions = false;

    /// <summary>Movement / pathing probes (CharacterMovement, navmesh checks).</summary>
    public static bool VerboseMovement = false;
}
