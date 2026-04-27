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

    public bool HasFreeSlot => FreeSlotCount > 0;

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
        // Wired to Character.EnterSleep in Task 3.
        if (c == null) return false;
        if (slotIndex < 0 || slotIndex >= _slots.Count) return false;
        var slot = _slots[slotIndex];
        if (slot.Occupant != null) return false;
        slot.Occupant = c;
        slot.ReservedBy = null;
        c.SetOccupyingFurniture(this);
        return true;
    }

    public void ReleaseSlot(int slotIndex)
    {
        // Wired to Character.ExitSleep in Task 3.
        if (slotIndex < 0 || slotIndex >= _slots.Count) return;
        var slot = _slots[slotIndex];
        if (slot.Occupant != null) slot.Occupant.SetOccupyingFurniture(null);
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

    public new bool Use(Character c)
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

    public new void Release()
    {
        // Release every slot that has an occupant or reservation.
        for (int i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].IsFree) ReleaseSlot(i);
        }
    }
}
