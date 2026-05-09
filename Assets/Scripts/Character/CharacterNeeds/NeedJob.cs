using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Drives "this NPC wants a job" planning. Server-only candidate discovery —
/// once per in-game day the server scans <see cref="BuildingManager"/> for an
/// available boss-owned vacant job and caches the (building, job) pair until
/// the next day flip. Replaces the previous per-tick FindAvailableJob poll
/// (~10 Hz × N NPCs) with a single OnNewDay event.
///
/// Lifecycle: this is a POCO (parented by the <c>CharacterNeeds</c>
/// MonoBehaviour). Subscription to <c>TimeManager.OnNewDay</c> is wired by
/// <c>CharacterNeeds.OnNetworkSpawn</c> via <see cref="TrySubscribeToOnNewDay"/>
/// and torn down by <c>OnNetworkDespawn</c> / <c>OnDestroy</c> via
/// <see cref="UnsubscribeFromOnNewDay"/>. Mirrors <see cref="NeedHunger"/>'s
/// TrySubscribeToPhase / UnsubscribeFromPhase pattern.
/// </summary>
public class NeedJob : CharacterNeed
{
    // L'urgence peut varier en fonction de la condition du PNJ (Richesse, Faim, etc.)
    // ou être fixe à 60 (Moyennement urgent, moins que la survie, plus que le blabla).
    private const float BASE_URGENCY = 60f;

    // Cached candidate from the most recent OnNewDay scan. Cleared at the start of each
    // new day; refilled if FindAvailableJob returns a hit. While both are null the need
    // is dormant — IsActive returns false and GetUrgency returns 0, so the GOAP planner
    // skips this need cleanly until tomorrow's re-scan.
    private CommercialBuilding _cachedBuilding;
    private Job _cachedJob;
    private bool _onNewDaySubscribed;

    public NeedJob(Character character) : base(character)
    {
    }

    /// <summary>
    /// Subscribes to <see cref="MWI.Time.TimeManager.OnNewDay"/> so the candidate
    /// (building, job) pair refreshes once per in-game day. Server-only — clients
    /// don't drive GOAP planning so they keep an empty cache (Need stays dormant).
    /// Idempotent + safe to call before TimeManager finishes its own Awake (no-ops
    /// until <c>TimeManager.Instance</c> is non-null).
    /// </summary>
    public void TrySubscribeToOnNewDay()
    {
        if (_onNewDaySubscribed) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (MWI.Time.TimeManager.Instance == null) return;

        MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        _onNewDaySubscribed = true;
    }

    /// <summary>
    /// Tears down the OnNewDay subscription. Idempotent. Called from
    /// <c>CharacterNeeds.OnNetworkDespawn</c> and <c>OnDestroy</c>.
    /// </summary>
    public void UnsubscribeFromOnNewDay()
    {
        if (!_onNewDaySubscribed) return;
        if (MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
        _onNewDaySubscribed = false;
    }

    public override bool IsActive()
    {
        // Lazy retry: CharacterNeeds.OnNetworkSpawn calls TrySubscribeToOnNewDay early in
        // the spawn pipeline, but TimeManager.Instance may not exist yet on a freshly-loaded
        // scene. IsActive is called every GOAP planning tick — cheap enough to re-poll the
        // singleton until subscription succeeds. Same pattern as FarmGrowthSystem.Update.
        if (!_onNewDaySubscribed) TrySubscribeToOnNewDay();

        // Active only when (a) we're an NPC, (b) the character has no job yet,
        // and (c) the OnNewDay scan has populated a candidate. Without (c) the
        // GOAP planner has no work to do — keep the need dormant rather than
        // emit an empty action list every tick.
        if (_character.Controller is PlayerController) return false;
        if (_character.CharacterJob == null || _character.CharacterJob.HasJob) return false;
        if (_cachedBuilding == null || _cachedJob == null) return false;
        return true;
    }

    public override float GetUrgency()
    {
        // Cache empty → Need is dormant; GOAP planner skips it cleanly.
        if (_cachedBuilding == null || _cachedJob == null) return 0f;
        return BASE_URGENCY;
    }

    public override GoapGoal GetGoapGoal()
    {
        return new GoapGoal("FindJob", new Dictionary<string, bool> { { "hasJob", true } }, (int)GetUrgency());
    }

    public override List<GoapAction> GetGoapActions()
    {
        var actions = new List<GoapAction>();

        if (_cachedBuilding == null || _cachedJob == null) return actions;

        // Re-validate the cached pair — it may have been claimed by another NPC since the
        // last OnNewDay scan, or the building's owner may have toggled hiring off / vacated
        // the role. If invalid, return empty actions; the cache stays — the Need becomes
        // effectively dormant until tomorrow's re-scan picks a different candidate.
        if (_cachedJob.IsAssigned || !_cachedBuilding.IsHiring || !_cachedBuilding.HasOwner)
        {
            if (NPCDebug.VerboseJobs)
                Debug.Log($"<color=orange>[NeedJob]</color> {_character?.CharacterName} cached job stale; idling until next day.");
            return actions;
        }

        if (NPCDebug.VerboseJobs)
            Debug.Log($"<color=yellow>[NeedJob]</color> {_character?.CharacterName} planning Apply at {_cachedBuilding.BuildingName}/{_cachedJob.JobTitle}.");

        actions.Add(new GoapAction_GoToBoss(_cachedBuilding.Owner));
        actions.Add(new GoapAction_AskForJob(_cachedBuilding, _cachedJob));

        return actions;
    }

    /// <summary>
    /// Server-only: refreshes the candidate building/job once per in-game day.
    /// Replaces the previous per-frame BuildingManager.FindAvailableJob call.
    /// Cache feeds GetUrgency (returns 0 when null) and GetGoapActions (uses the
    /// cached pair after an IsAssigned/IsHiring/HasOwner re-validation).
    /// </summary>
    private void HandleNewDay()
    {
        // Defensive: subscribe path already gates IsServer, but a host-shutdown race
        // could leave the handler bound while NetworkManager flips to client. Bail.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        // If the character already has a job, skip the scan — Need is satisfied.
        // Clear the cache so IsActive/GetUrgency return false/0 immediately.
        if (_character != null && _character.CharacterJob != null && _character.CharacterJob.HasJob)
        {
            _cachedBuilding = null;
            _cachedJob = null;
            return;
        }

        if (BuildingManager.Instance == null)
        {
            _cachedBuilding = null;
            _cachedJob = null;
            return;
        }

        var (building, job) = BuildingManager.Instance.FindAvailableJob<Job>(requireBoss: true);
        _cachedBuilding = building;
        _cachedJob = job;

        if (NPCDebug.VerboseJobs)
        {
            string label = building != null && job != null
                ? $"{building.BuildingName}/{job.JobTitle}"
                : "(none)";
            Debug.Log($"<color=yellow>[NeedJob]</color> {_character?.CharacterName} OnNewDay scan → cached {label}.");
        }
    }
}
