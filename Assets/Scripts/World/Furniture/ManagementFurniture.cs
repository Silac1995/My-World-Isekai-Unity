using UnityEngine;
using MWI.UI.Notifications;

/// <summary>
/// Owner's management desk for a <see cref="CommercialBuilding"/>. Owner walks up, presses E,
/// <see cref="MWI.UI.Management.UI_OwnerManagementPanel"/> opens for the parent building. Non-owners get a toast
/// "Only the owner can use this management desk."
///
/// Replaces the v1 "Manage Hiring..." menu entry on every NPC interaction (Plan 2 Task 8) —
/// the menu entry stays as a fallback only when <c>CommercialBuilding._managementFurniture</c>
/// is null (Plan 2.5 Task 3).
///
/// **Future driveable-entity migration:** this furniture is a deliberate precursor to the
/// parallel-session "driveable entities" system. v1 opens UI on E-press immediately; the
/// future migration will re-parent this class under <see cref="OccupiableFurniture"/> so
/// the player gets seated at the desk and the UI opens as a side-effect of occupancy.
/// Public API stays stable across the migration — only the internals of
/// <see cref="OnInteract"/> change (and the parent type).
///
/// No NetworkBehaviour sibling needed — this furniture owns no replicated state. The panel
/// it opens reads <see cref="CommercialBuilding"/>'s already-replicated <c>_isHiring</c> +
/// <c>_helpWantedFurniture</c> state.
///
/// Inherits plain <see cref="Furniture"/> (no occupancy) post 2026-05-08 ISP refactor —
/// the management desk is interaction-only; no character "occupies" it today. Overrides
/// <see cref="Furniture.OnInteract"/> instead of the legacy <c>Use</c> method.
/// </summary>
public class ManagementFurniture : Furniture
{
    /// <summary>
    /// Owner-only OnInteract. Resolves parent CommercialBuilding via GetComponentInParent,
    /// validates owner identity, opens the management panel. NPCs silent-success; remote
    /// clients filtered by the IsOwner gate (mirrors DisplayTextFurniture's pattern).
    /// </summary>
    public override bool OnInteract(Character character)
    {
        if (character == null) return false;

        // Remote-client gate: only the owning peer pops UI.
        if (character.IsSpawned && !character.IsOwner) return true;

        // NPCs: silent success (no UI pop).
        if (!character.IsPlayer()) return true;

        var building = GetComponentInParent<CommercialBuilding>();
        if (building == null)
        {
            Debug.LogWarning($"[ManagementFurniture] {name} not parented under a CommercialBuilding.");
            return false;
        }

        // Multi-owner aware: route through Room.IsOwner(Character) — compares the character's
        // UUID against the FULL replicated _ownerIds NetworkList. The legacy
        // `building.Owner != character` check only matched the FIRST owner (the singular
        // Owner getter returns `_ownerIds[0]`), so a secondary owner added via
        // `CommercialBuilding.AddOwner` (e.g. the dev console's "[DEV] Add Owner" button
        // or any future co-ownership flow) was incorrectly rejected with the toast below.
        if (!building.IsOwner(character))
        {
            UI_Toast.Show("Only the owner can use this management desk.", ToastType.Warning, duration: 3f, title: "Not your desk");
            return true;
        }

        MWI.UI.Management.UI_OwnerManagementPanel.Show(building);
        return true;
    }
}
