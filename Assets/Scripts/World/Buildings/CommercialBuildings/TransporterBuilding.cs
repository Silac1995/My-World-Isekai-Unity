using UnityEngine;

/// <summary>
/// Building responsible for accepting logistics orders and dispatching transporters to move
/// resources across the city.
/// </summary>
public class TransporterBuilding : CommercialBuilding
{
    // Per existing nomenclature, we can reuse commercial
    public override BuildingType BuildingType => BuildingType.Commercial;

    [Header("Transporter Config")]
    [SerializeField] private int _transporterCount = 3;

    protected override void InitializeJobs()
    {
        // 1 Manager (to take orders)
        _jobs.Add(new JobLogisticsManager("Head of Logistics"));

        // N Transporters (to physically carry the crates)
        for (int i = 0; i < _transporterCount; i++)
        {
            _jobs.Add(new JobTransporter($"Transporter {i + 1}"));
        }

        Debug.Log($"<color=green>[TransporterBuilding]</color> {buildingName} initialized with 1 Manager and {_transporterCount} Transporters.");
    }
}
