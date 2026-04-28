using MWI.Needs;
using MWI.Time;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ground sleep — character lies down where they stand. Short repeating action
/// (5s real-time per tick). On each tick, applies a small live restoration chunk
/// to stamina + NeedSleep. Cancels when a TimeSkip starts (offline restoration
/// takes over) and on combat / damage / movement.
///
/// Save-on-wake is owned by TimeSkipController (not this action) — only legitimate
/// time-skipped sleep saves the player profile.
/// </summary>
public class CharacterAction_Sleep : CharacterAction
{
    private const float TICK_DURATION = 5f;

    public CharacterAction_Sleep(Character character) : base(character, TICK_DURATION) { }

    public override string ActionName => "Sleep";

    public override bool CanExecute()
    {
        if (character == null) return false;
        if (TimeSkipController.Instance != null && TimeSkipController.Instance.IsSkipping) return false;
        return true;
    }

    public override void OnStart()
    {
        // Server-only state mutation. Re-enqueue (every 5s tick) skips the call so
        // EnterSleep doesn't emit its "already sleeping" warning every tick — the
        // first-tick call is the one that flips IsSleeping.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer
            && !character.IsSleeping)
        {
            character.EnterSleep(character.transform);
        }

        // Cancel ourselves the moment a time-skip starts — offline restoration takes over.
        if (TimeSkipController.Instance != null)
        {
            TimeSkipController.Instance.OnSkipStarted += HandleSkipStarted;
        }
    }

    public override void OnApplyEffect()
    {
        // Server applies the restoration chunk. Live action ticks complement
        // (don't replace) the per-hour macro-sim restoration.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            // Still unsubscribe on natural completion to avoid a handler leak
            // — Finish() flows through CleanupAction, NOT OnCancel.
            Unsubscribe();
            return;
        }

        ApplyRestore();
        // Critical: natural Finish() does NOT invoke OnCancel, so the
        // OnSkipStarted subscription must be cleaned up here every tick.
        // Re-enqueue resubscribes on the next OnStart.
        Unsubscribe();
    }

    public override void OnCancel()
    {
        Unsubscribe();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            character.ExitSleep();  // idempotent
        }
    }

    private void HandleSkipStarted(int hours)
    {
        Unsubscribe();
        // Force-cancel via the action layer so OnCancel fires once.
        character.CharacterActions?.ClearCurrentAction();
    }

    private void Unsubscribe()
    {
        if (TimeSkipController.Instance != null)
            TimeSkipController.Instance.OnSkipStarted -= HandleSkipStarted;
    }

    private void ApplyRestore()
    {
        var sleep = character.CharacterNeeds?.GetNeed<NeedSleep>();
        sleep?.IncreaseValue(NeedSleepMath.LIVE_GROUND_RESTORE_PER_TICK);

        // IncreaseCurrentAmountPercent takes 0.0–1.0; LIVE_GROUND_RESTORE_PER_TICK is on a
        // 0–100 NeedSleep scale, so dividing by 100 gives the equivalent stamina fraction.
        var stamina = character.Stats?.Stamina;
        stamina?.IncreaseCurrentAmountPercent(NeedSleepMath.LIVE_GROUND_RESTORE_PER_TICK * 0.01f);
    }
}
