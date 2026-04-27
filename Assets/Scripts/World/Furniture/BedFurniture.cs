using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bed furniture with a modular per-prefab slot list. Single-bed prefab = 1 slot,
/// double-bed = 2, family-bed = 4, etc. Slot count is baked into the prefab via
/// the serialized <c>_slots</c> list — no per-prefab code.
///
/// Slot-aware lifecycle is preferred (<see cref="ReserveSlot"/> / <see cref="UseSlot"/>
/// / <see cref="ReleaseSlot"/>). Base <see cref="Furniture.Reserve"/> / <see cref="Furniture.Use"/>
/// / <see cref="Furniture.Release"/> are overridden to pick the first free slot for
/// backward-compat with legacy single-slot callers (e.g. existing SleepBehaviour fallback).
/// </summary>
public class BedFurniture : Furniture
{
    [Header("Bed")]
    [SerializeField] private List<BedSlot> _slots = new List<BedSlot>();

    public IReadOnlyList<BedSlot> Slots => _slots;
    public int SlotCount => _slots.Count;

    public int FreeSlotCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].IsFree) n++;
            return n;
        }
    }

    public bool HasFreeSlot => FindFreeSlotIndex() >= 0;

    public int FindFreeSlotIndex()
    {
        for (int i = 0; i < _slots.Count; i++) if (_slots[i].IsFree) return i;
        return -1;
    }

    public int GetSlotIndexFor(Character c)
    {
        if (c == null) return -1;
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].Occupant == c || _slots[i].ReservedBy == c) return i;
        return -1;
    }

    public bool ReserveSlot(int slotIndex, Character c)
    {
        if (c == null) return false;
        if (slotIndex < 0 || slotIndex >= _slots.Count) return false;
        var slot = _slots[slotIndex];
        if (!slot.IsFree) return false;
        slot.ReservedBy = c;
        return true;
    }

    public bool UseSlot(int slotIndex, Character c)
    {
        if (c == null) return false;
        if (slotIndex < 0 || slotIndex >= _slots.Count) return false;
        var slot = _slots[slotIndex];
        if (slot.Occupant != null)
        {
            Debug.LogWarning($"<color=orange>[BedFurniture]</color> Slot {slotIndex} on {FurnitureName} already occupied by {slot.Occupant.CharacterName}.");
            return false;
        }
        if (slot.ReservedBy != null && slot.ReservedBy != c)
        {
            Debug.LogWarning($"<color=orange>[BedFurniture]</color> Slot {slotIndex} on {FurnitureName} reserved by {slot.ReservedBy.CharacterName}, not {c.CharacterName}.");
            return false;
        }
        if (slot.Anchor == null)
        {
            Debug.LogError($"<color=red>[BedFurniture]</color> Slot {slotIndex} on {FurnitureName} has no Anchor authored. Cannot UseSlot.");
            return false;
        }

        slot.Occupant = c;
        slot.ReservedBy = null;
        c.SetOccupyingFurniture(this);
        c.EnterSleep(slot.Anchor);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"<color=cyan>[BedFurniture]</color> {c.CharacterName} occupied slot {slotIndex} on {FurnitureName}.");
#endif
        return true;
    }

    public void ReleaseSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Count) return;
        var slot = _slots[slotIndex];
        if (slot.Occupant != null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"<color=cyan>[BedFurniture]</color> {slot.Occupant.CharacterName} released slot {slotIndex} on {FurnitureName}.");
#endif
            slot.Occupant.ExitSleep();
            slot.Occupant.SetOccupyingFurniture(null);
        }
        slot.Occupant = null;
        slot.ReservedBy = null;
    }

    // ── Override base Furniture single-slot API for backward-compat ──

    public override bool IsFree() => HasFreeSlot;

    public override bool Reserve(Character c)
    {
        int idx = FindFreeSlotIndex();
        if (idx < 0)
        {
            Debug.LogWarning($"<color=orange>[BedFurniture]</color> Reserve fallback: no free slot on {FurnitureName}.");
            return false;
        }
        return ReserveSlot(idx, c);
    }

    public override bool Use(Character c)
    {
        int idx = GetSlotIndexFor(c);
        if (idx < 0) idx = FindFreeSlotIndex();
        if (idx < 0)
        {
            Debug.LogWarning($"<color=orange>[BedFurniture]</color> Use fallback: no free slot on {FurnitureName}.");
            return false;
        }
        return UseSlot(idx, c);
    }

    public override void Release()
    {
        // Release every slot that has an occupant or reservation.
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].IsFree) ReleaseSlot(i);
        }
    }
}
