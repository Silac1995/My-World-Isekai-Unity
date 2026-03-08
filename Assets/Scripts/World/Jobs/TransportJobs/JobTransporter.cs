using System.Linq;
using UnityEngine;

/// <summary>
/// Métier du livreur. Demande du travail à son JobLogisticsManager interne, puis part en livraison.
/// Couplé à un Behaviour Tree (BTAction_PerformTransport) qui effectue le déplacement physiquement.
/// </summary>
public class JobTransporter : Job
{
    private string _customTitle;
    public override string JobTitle => _customTitle;
    public override JobCategory Category => JobCategory.Transporter;

    // La commande courante que l'employé est en train de livrer
    public BuyOrder CurrentOrder { get; private set; }

    public JobTransporter(string title = "Transporter")
    {
        _customTitle = title;
    }

    public override void Execute()
    {
        // La logique d'exécution pure est gérée par le BT.
        // Ici, on pourrait s'assurer qu'il va chercher un job s'il n'en a pas et que le BT le demande.
    }

    /// <summary>
    /// Si le transporteur n'a pas de commande en cours, il interroge le Manager de son bâtiment
    /// pour lui demander s'il y a des commandes en attente.
    /// Retourne vrai s'il a du travail (une commande assignée).
    /// </summary>
    public override bool HasWorkToDo()
    {
        if (CurrentOrder != null && !CurrentOrder.IsCompleted)
        {
            return true;
        }

        if (_workplace != null)
        {
            var manager = _workplace.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
            if (manager != null)
            {
                BuyOrder next = manager.GetNextAvailableOrder();
                if (next != null)
                {
                    AssignOrder(next);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Assigne une commande spécifique à ce transporteur.
    /// </summary>
    public void AssignOrder(BuyOrder order)
    {
        CurrentOrder = order;
        if (order != null)
        {
            Debug.Log($"<color=green>[JobTransporter]</color> {_worker?.CharacterName} commence la demande: Livrer {order.ItemToTransport.ItemName} à {order.Destination.BuildingName}.");
        }
    }

    /// <summary>
    /// Appelé par le Behaviour Tree lorsque le transporteur a fini de déplacer un lot.
    /// </summary>
    public void NotifyDeliveryProgress(int amount)
    {
        if (CurrentOrder != null)
        {
            var manager = _workplace.GetJobsOfType<JobLogisticsManager>().FirstOrDefault();
            if (manager != null)
            {
                manager.UpdateOrderProgress(CurrentOrder, amount);
            }

            if (CurrentOrder.IsCompleted)
            {
                CurrentOrder = null;
            }
        }
    }

    public void CancelCurrentOrder()
    {
        Debug.Log($"<color=orange>[JobTransporter]</color> {_worker?.CharacterName} annule sa livraison en cours.");
        CurrentOrder = null;
    }

    public override string CurrentActionName => CurrentOrder != null ? $"Livraison : {CurrentOrder.ItemToTransport.ItemName}" : "En attente de commandes";
}
