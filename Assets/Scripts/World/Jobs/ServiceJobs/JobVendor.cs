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
        // 1. On ne peut exécuter le job que si on est sur notre lieu de travail
        if (_workplace is ShopBuilding shop && _worker.Controller is NPCController npc)
        {
            // 2. On vérifie si on est déjà en train de faire le comportement de vendeur
            // Si non, on le push sur la pile du NPCController
            if (!npc.HasBehaviour<BTVendorBehaviour>())
            {
                npc.PushBehaviour(new BTVendorBehaviour(npc, shop));
            }
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is ShopBuilding;
    }
}
