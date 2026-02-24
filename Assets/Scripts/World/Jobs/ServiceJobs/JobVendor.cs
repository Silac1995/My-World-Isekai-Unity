using UnityEngine;

/// <summary>
/// Job de Vendeur : gère les achats des clients dans un ShopBuilding.
/// Attend les clients, traite les achats et encaisse.
/// </summary>
public class JobVendor : Job
{
    public override string JobTitle => "Vendeur";
    public override JobCategory Category => JobCategory.Service;

    public override void Execute()
    {
        // Logique spécifique :
        // 1. Attendre un client au comptoir
        // 2. Traiter l'achat
        // 3. Encaisser
        if (_workplace is ShopBuilding shop)
        {
            // TODO: Implémenter la logique de vente
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is ShopBuilding;
    }
}
