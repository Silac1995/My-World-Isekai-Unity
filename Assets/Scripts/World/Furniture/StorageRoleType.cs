using System.Collections.Generic;

/// <summary>
/// The role assigned to a single <see cref="StorageFurniture"/> by its parent
/// <see cref="CommercialBuilding"/>'s owner. Each storage carries exactly one
/// <see cref="StorageRoleType"/> value (or <see cref="None"/>) — mutually
/// exclusive at the storage level. Multiple storages within the same building
/// can independently share the same role (e.g., 3 sell-shelves), so a
/// building-side query for "storages with role X" returns a list, not a singleton.
///
/// Authored 2026-05-08 as part of the unified storage-role system that replaces
/// the dedicated <c>ShopShelvesTab</c> + the inspector-authored
/// <see cref="CommercialBuilding.ToolStorage"/> singleton with one generic
/// owner-driven assignment surface. See
/// <c>wiki/projects/management-panel-followups.md</c> §1 for the design lock
/// and <c>.planning/sketches/001-storages-tab/</c> for the UI shape.
/// </summary>
public enum StorageRoleType
{
    /// <summary>Default — the storage carries no special role; just a regular drop/pickup chest.</summary>
    None = 0,

    /// <summary>Building's preferred bin for tools — the deposit pre-pass routes tool items here when free space exists.</summary>
    ToolStorage = 1,

    /// <summary>Building's general-purpose inventory bin — preferred destination for general goods (raw materials, finished items, etc.).</summary>
    InventoryStorage = 2,

    /// <summary>Customer-facing sell-shelf — only meaningful on <c>ShopBuilding</c>. Items here are pulled by <c>CharacterAction_BuyFromShop</c> at commit time.</summary>
    SellShelf = 3,

    // Future role types append here. Reserve [4, 100] for shipped roles, [100, 200] for experimental
    // additions, [200+] for project-specific extensions.
}

/// <summary>
/// Static metadata for a <see cref="StorageRoleType"/> — display name + icon glyph.
/// Used by the <c>StorageRolesTab</c> UI to render dropdown options.
///
/// Per-subtype role availability is decided at the building level via
/// <c>CommercialBuilding.SupportedStorageRoles</c>; this struct just describes
/// how a single role looks in the UI.
/// </summary>
[System.Serializable]
public struct StorageRoleDescriptor
{
    public StorageRoleType Type;
    public string DisplayName;
    /// <summary>Single-char glyph rendered next to the option name in the dropdown — kept short for tight layout.</summary>
    public string Icon;

    public StorageRoleDescriptor(StorageRoleType type, string displayName, string icon)
    {
        Type = type;
        DisplayName = displayName;
        Icon = icon;
    }
}

/// <summary>
/// Default catalog of role descriptors. Buildings expose a SUBSET of this catalog
/// via their <c>SupportedStorageRoles</c> — generic <c>CommercialBuilding</c>
/// exposes None / Tool / Inventory; <c>ShopBuilding</c> additionally exposes SellShelf.
/// </summary>
public static class StorageRoleCatalog
{
    public static readonly StorageRoleDescriptor None             = new StorageRoleDescriptor(StorageRoleType.None,             "None",              "—");
    public static readonly StorageRoleDescriptor ToolStorage      = new StorageRoleDescriptor(StorageRoleType.ToolStorage,      "Tool Storage",      "⚒");
    public static readonly StorageRoleDescriptor InventoryStorage = new StorageRoleDescriptor(StorageRoleType.InventoryStorage, "Inventory Storage", "📦");
    public static readonly StorageRoleDescriptor SellShelf        = new StorageRoleDescriptor(StorageRoleType.SellShelf,        "Sell-Shelf",        "⛀");

    /// <summary>Generic-CommercialBuilding default role list (no Sell-Shelf).</summary>
    public static readonly IReadOnlyList<StorageRoleDescriptor> Generic = new[]
    {
        None, ToolStorage, InventoryStorage,
    };

    /// <summary>ShopBuilding role list — generic + Sell-Shelf.</summary>
    public static readonly IReadOnlyList<StorageRoleDescriptor> Shop = new[]
    {
        None, ToolStorage, InventoryStorage, SellShelf,
    };

    /// <summary>O(N) lookup; N = 4 today, never gets large enough to warrant a dictionary.</summary>
    public static StorageRoleDescriptor Get(StorageRoleType type)
    {
        switch (type)
        {
            case StorageRoleType.ToolStorage:      return ToolStorage;
            case StorageRoleType.InventoryStorage: return InventoryStorage;
            case StorageRoleType.SellShelf:        return SellShelf;
            default:                               return None;
        }
    }
}
