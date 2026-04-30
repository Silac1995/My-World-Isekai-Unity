using UnityEngine;
using MWI.UI.Notifications;

/// <summary>
/// Owner's management desk for a <see cref="CommercialBuilding"/>. Owner walks up, presses E,
/// <see cref="UI_OwnerHiringPanel"/> opens for the parent building. Non-owners get a toast
/// "Only the owner can use this management desk."
///
/// Replaces the v1 "Manage Hiring..." menu entry on every NPC interaction (Plan 2 Task 8) —
/// the menu entry stays as a fallback only when <c>CommercialBuilding._managementFurniture</c>
/// is null (Plan 2.5 Task 3).
///
/// **Future driveable-entity migration:** this furniture is a deliberate precursor to the
/// parallel-session "driveable entities" system. v1 opens UI on E-press immediately; the
/// future migration replaces <see cref="Use"/> with an "occupy this driveable entity" call
/// (the player gets seated at the desk; the UI opens as a side-effect of being seated;
/// exiting the desk closes the UI). Public API stays stable across the migration — only
/// the internals of <see cref="Use"/> change.
///
/// No NetworkBehaviour sibling needed — this furniture owns no replicated state. The panel
/// it opens reads <see cref="CommercialBuilding"/>'s already-replicated <c>_isHiring</c> +
/// <c>_helpWantedFurniture</c> state.
/// </summary>
public class ManagementFurniture : Furniture
{
    /// <summary>
    /// Owner-only Use. Resolves parent CommercialBuilding via GetComponentInParent, validates
    /// owner identity, opens the hiring panel. NPCs silent-success; remote clients filtered
    /// by the IsOwner gate (mirrors DisplayTextFurniture.Use's pattern).
    /// </summary>
    public override bool Use(Character character)
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

        if (!building.HasOwner || building.Owner != character)
        {
            UI_Toast.Show("Only the owner can use this management desk.", ToastType.Warning, duration: 3f, title: "Not your desk");
            return true;
        }

        UI_OwnerHiringPanel.Show(building);
        return true;
    }
}
