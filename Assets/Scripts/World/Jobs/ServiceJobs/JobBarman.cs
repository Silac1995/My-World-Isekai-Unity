using UnityEngine;

/// <summary>
/// Job de Barman : prépare les boissons dans un BarBuilding.
/// Vérifie les commandes en attente et les prépare.
/// </summary>
public class JobBarman : Job
{
    public override string JobTitle => "Barman";
    public override JobCategory Category => JobCategory.Service;

    public override void Execute()
    {
        // Logique spécifique : préparer les boissons
        // Vérifier s'il y a des commandes en attente dans le BarBuilding
        // Préparer la boisson → la marquer comme prête pour le serveur
        if (_workplace is BarBuilding bar)
        {
            // TODO: Implémenter la logique de préparation des boissons
            // var order = bar.GetNextOrder();
            // if (order != null) { préparer la boisson... }
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is BarBuilding;
    }
}
