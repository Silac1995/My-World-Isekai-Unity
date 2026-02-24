using UnityEngine;

/// <summary>
/// Bâtiment de type Shop.
/// Nécessite un Vendeur pour fonctionner et vendre ses produits aux clients.
/// </summary>
public class ShopBuilding : CommercialBuilding
{
    public override BuildingType BuildingType => BuildingType.Shop;

    protected override void InitializeJobs()
    {
        _jobs.Add(new JobVendor());

        Debug.Log($"<color=magenta>[Shop]</color> {buildingName} initialisé avec 1 Vendeur.");
    }

    /// <summary>
    /// Récupère le vendeur de ce shop.
    /// </summary>
    public JobVendor GetVendor()
    {
        foreach (var job in _jobs)
        {
            if (job is JobVendor vendor) return vendor;
        }
        return null;
    }
}
