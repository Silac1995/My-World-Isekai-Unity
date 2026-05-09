using MWI.Needs;
using MWI.Time;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bed sleep — character occupies a slot on a <see cref="BedFurniture"/>.
/// On start, calls <c>bed.UseSlot(slotIndex, character)</c> which chains to
/// <c>Character.EnterSleep(slot.Anchor)</c>. On cancel, releases the slot
/// (which chains to <c>ExitSleep</c>).
///
/// 5s repeating action. On each tick, applies a bed-rate restore chunk to
/// stamina + NeedSleep (~2.5× ground rate). Cancels on TimeSkip start so
/// the offline macro-sim restoration takes over for the skip duration.
///
/// Save-on-wake is owned by TimeSkipController (not this action).
/// </summary>
public class CharacterAction_SleepOnFurniture : CharacterAction
{
    private const float TICK_DURATION = 5f;

    private readonly BedFurniture _bed;
    private readonly int _slotIndex;
    private bool _slotAcquired;

    public CharacterAction_SleepOnFurniture(Character character, BedFurniture bed, int slotIndex)
        : base(character, TICK_DURATION)
    {
        _bed = bed;
        _slotIndex = slotIndex;
    }

    public override string ActionName => "Sleep (bed)";

    public override bool CanExecute()
    {
        if (character == null || _bed == null) return false;
        if (_slotIndex < 0 || _slotIndex >= _bed.SlotCount) return false;
        if (TimeSkipController.Instance != null && TimeSkipController.Instance.IsSkipping) return false;

        // Slot must be free OR already held by this character (re-enqueue case).
        var slot = _bed.Slots[_slotIndex];
        if (slot.Occupant != null && slot.Occupant != character) return false;
        if (slot.ReservedBy != null && slot.ReservedBy != character) return false;
        return true;
    }

    public override void OnStart()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            // C2 fix: if we're already the slot's occupant (re-enqueue case), skip the
            // UseSlot call — bed.UseSlot internally calls EnterSleep which would
            // emit "already sleeping" warnings on every re-enqueue tick.
            if (_bed.Slots[_slotIndex].Occupant == character)
            {
                _slotAcquired = true;  // we already had it from a prior tick
            }
            else
            {
                _slotAcquired = _bed.UseSlot(_slotIndex, character);
                if (!_slotAcquired)
                {
                    Debug.LogWarning($"<color=orange>[CharacterAction_SleepOnFurniture]</color> {character.CharacterName} failed to acquire slot {_slotIndex} on {_bed.FurnitureName}.");
                    Finish();  // bail
                    return;
                }
            }
        }

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
            // C1 fix: still unsubscribe on natural completion to avoid a handler leak —
            // Finish() flows through CleanupAction, NOT OnCancel.
            Unsubscribe();
            return;
        }

        if (_slotAcquired) ApplyRestore();

        // C1 fix: natural Finish() does NOT invoke OnCancel, so the
        // OnSkipStarted subscription must be cleaned up here every tick.
        // Re-enqueue resubscribes on the next OnStart.
        Unsubscribe();
    }

    public override void OnCancel()
    {
        Unsubscribe();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && _slotAcquired)
        {
            // ReleaseSlot internally calls Character.ExitSleep (idempotent).
            _bed.ReleaseSlot(_slotIndex);
        }
    }

    private void HandleSkipStarted(int hours)
    {
        Unsubscribe();
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
        sleep?.IncreaseValue(NeedSleepMath.LIVE_BED_RESTORE_PER_TICK);

        // Same scale conversion as CharacterAction_Sleep: LIVE_BED_RESTORE_PER_TICK is on a
        // 0–100 NeedSleep scale, divide by 100 for the stamina fraction.
        var stamina = character.Stats?.Stamina;
        stamina?.IncreaseCurrentAmountPercent(NeedSleepMath.LIVE_BED_RESTORE_PER_TICK * 0.01f);
    }
}
