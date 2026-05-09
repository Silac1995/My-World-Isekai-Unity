using UnityEngine;

/// <summary>
/// Marker furniture for a CommercialBuilding's punch-in / punch-out station.
/// Characters (players + NPCs) must physically touch this furniture to call
/// <see cref="CommercialBuilding.WorkerStartingShift"/> /
/// <see cref="CommercialBuilding.WorkerEndingShift"/>. The actual Interact
/// logic lives on <see cref="TimeClockFurnitureInteractable"/>; this class
/// exists purely as a typed handle so a parent CommercialBuilding can
/// discover its clock via <c>GetComponentInChildren&lt;TimeClockFurniture&gt;</c>.
///
/// Inherits <see cref="OccupiableFurniture"/> (post 2026-05-08 ISP refactor) — the
/// puncher briefly occupies the clock for the duration of <c>Action_PunchIn</c> /
/// <c>Action_PunchOut</c>; the action's <c>OnActionFinished</c> calls
/// <see cref="OccupiableFurniture.Release"/> to free the slot. This prevents a
/// second worker from queuing against the same clock mid-punch.
/// </summary>
public class TimeClockFurniture : OccupiableFurniture
{
}
