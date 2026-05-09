/// <summary>
/// One catalog entry inside a saved <see cref="ShopBuilding"/>. Lives on
/// <c>BuildingSaveData.ShopCatalog</c> (populated only for shop buildings).
///
/// <para>
/// We do NOT subclass <c>BuildingSaveData</c> to add shop-specific state — the project
/// serializes building saves through Newtonsoft <c>JsonConvert.SerializeObject</c>
/// without <c>TypeNameHandling</c> (see <c>SaveFileHandler.WriteWorldAsync</c>), which
/// drops derived-type information on deserialize. Following the existing convention
/// (commercial-only <c>Employees</c>, <c>OwnerCharacterIds</c>, etc.), shop-specific
/// fields live on <see cref="MWI.WorldSystem.BuildingSaveData"/> and are populated only
/// when the source building is a <see cref="ShopBuilding"/>.
/// </para>
///
/// <para>
/// Sell-shelves are persisted as a list of <see cref="MWI.WorldSystem.BuildingSaveData.ComputeFurnitureKey"/>
/// strings on <c>BuildingSaveData.SellShelfFurnitureKeys</c> — the same key scheme used
/// for <c>StorageFurnitures</c>, so a single resolution helper handles both. No DTO is
/// needed for shelves.
/// </para>
/// </summary>
[System.Serializable]
public struct ShopCatalogEntrySaveEntry
{
    /// <summary>Resolves to <see cref="ItemSO"/> via Resources/Data/Item lookup on load.</summary>
    public string itemId;

    /// <summary>Authored maximum stock for this item — drives logistics restock targets.</summary>
    public int maxStock;

    /// <summary>0 means "use ItemSO.BasePrice" (sentinel matches <see cref="ShopItemEntry.PriceOverride"/>).</summary>
    public int priceOverride;
}
