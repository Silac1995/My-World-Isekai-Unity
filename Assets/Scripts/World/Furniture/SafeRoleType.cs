using System.Collections.Generic;

/// <summary>
/// Role a <see cref="SafeFurniture"/> instance can carry inside a building.
/// Mirrors <see cref="StorageRoleType"/> but lives on a separate, dedicated enum
/// because the role catalogs are semantically disjoint — a safe never plays a
/// "ToolStorage" or "SellShelf" role and a storage chest never plays a
/// "Treasury" role. Keeping them split prevents a Forge owner from seeing
/// "Treasury" inside their crate dropdown (and vice-versa).
///
/// Authored 2026-05-09 as part of the unified B2B shop-buy logistics path:
/// the building's Treasury (aggregate currency funds) derives from every
/// child <see cref="SafeFurniture"/> whose <see cref="SafeFurniture.Role"/>
/// equals <see cref="Treasury"/>.
/// </summary>
public enum SafeRoleType
{
    /// <summary>Safe is present but unassigned; its balance does NOT count toward the
    /// building's treasury. Default for fresh-placed safes; the owner (or the NPC
    /// LogisticsManager via <c>BuildingLogisticsManager.AssignStorageRolesForShift</c>)
    /// flips it to <see cref="Treasury"/> to bring it online.</summary>
    None = 0,

    /// <summary>Safe's balance contributes to <c>CommercialBuilding.GetTreasuryBalance</c>.
    /// The aggregate treasury is the sum across every Treasury-role safe in the building.
    /// Multiple safes can independently share the Treasury role.</summary>
    Treasury = 1,
}

/// <summary>
/// UI-render metadata for a <see cref="SafeRoleType"/>. Sibling of
/// <see cref="StorageRoleDescriptor"/> — kept parallel rather than merged so
/// the two role taxonomies evolve independently.
/// </summary>
public struct SafeRoleDescriptor
{
    public SafeRoleType Type;
    public string DisplayName;
    public string Icon; // sprite resource path or empty — designer wires later

    public SafeRoleDescriptor(SafeRoleType type, string displayName, string icon = "")
    {
        Type = type;
        DisplayName = displayName;
        Icon = icon;
    }
}

/// <summary>
/// Static catalog of supported <see cref="SafeRoleDescriptor"/> sets, keyed by
/// the consuming building type. Today every <see cref="CommercialBuilding"/>
/// exposes the same <see cref="Generic"/> catalog — Treasury is the only
/// non-None role — but the catalog indirection keeps the door open for
/// future subtypes (e.g. a Bank exposing PettyCash / Reserve / Vault tiers).
/// </summary>
public static class SafeRoleCatalog
{
    public static readonly SafeRoleDescriptor None     = new SafeRoleDescriptor(SafeRoleType.None,     "None");
    public static readonly SafeRoleDescriptor Treasury = new SafeRoleDescriptor(SafeRoleType.Treasury, "Treasury");

    /// <summary>Default catalog for every <see cref="CommercialBuilding"/>. None + Treasury.</summary>
    public static readonly IReadOnlyList<SafeRoleDescriptor> Generic = new List<SafeRoleDescriptor>
    {
        None,
        Treasury,
    };
}
