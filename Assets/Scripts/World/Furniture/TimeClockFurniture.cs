using UnityEngine;

/// <summary>
/// Marker furniture for a CommercialBuilding's punch-in / punch-out station.
/// Characters (players + NPCs) must physically touch this furniture to call
/// <see cref="CommercialBuilding.WorkerStartingShift"/> /
/// <see cref="CommercialBuilding.WorkerEndingShift"/>. The actual Interact
/// logic lives on <see cref="TimeClockFurnitureInteractable"/>; this class
/// exists purely as a typed handle so a parent CommercialBuilding can
/// discover its clock via <c>GetComponentInChildren&lt;TimeClockFurniture&gt;</c>.
/// </summary>
public class TimeClockFurniture : Furniture
{
}
