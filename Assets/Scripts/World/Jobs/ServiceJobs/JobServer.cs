using UnityEngine;

/// <summary>
/// Job de Serveur : prend les commandes des clients et leur sert les boissons.
/// Fait le lien entre les clients et le barman.
/// </summary>
public class JobServer : Job
{
    public override string JobTitle => "Serveur";
    public override JobCategory Category => JobCategory.Service;

    public override void Execute()
    {
        // Logique spécifique :
        // 1. Prendre les commandes des clients dans la zone
        // 2. Récupérer les boissons prêtes chez le barman
        // 3. Servir les clients
        if (_workplace is BarBuilding bar)
        {
            // TODO: Implémenter la logique de service
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is BarBuilding;
    }
}
