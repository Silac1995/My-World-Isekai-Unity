using UnityEngine;

/// <summary>
/// Bâtiment responsable d'accepter des commandes logistiques et de dépêcher des transporteurs pour déplacer
/// les ressources à travers la ville.
/// </summary>
public class TransporterBuilding : CommercialBuilding
{
    // Selon la nomenclature existante, on peut réutiliser commercial
    public override BuildingType BuildingType => BuildingType.Commercial;

    [Header("Transporter Config")]
    [SerializeField] private int _transporterCount = 3;

    protected override void InitializeJobs()
    {
        // 1 Manager (pour prendre les commandes)
        _jobs.Add(new JobLogisticsManager("Head of Logistics"));

        // N Transporters (pour transporter physiquement les caisses)
        for (int i = 0; i < _transporterCount; i++)
        {
            _jobs.Add(new JobTransporter($"Transporter {i + 1}"));
        }

        Debug.Log($"<color=green>[TransporterBuilding]</color> {buildingName} initialisé avec 1 Manager et {_transporterCount} Transporters.");
    }
}
