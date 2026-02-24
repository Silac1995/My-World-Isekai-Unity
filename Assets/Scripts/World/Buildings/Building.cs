using UnityEngine;

/// <summary>
/// Classe abstraite mère de tous les bâtiments.
/// Hérite de Zone pour bénéficier du trigger, du NavMesh sampling, et du tracking des personnages.
/// </summary>
public abstract class Building : Zone
{
    [Header("Building Info")]
    [SerializeField] protected string buildingName;

    public string BuildingName => buildingName;
    public abstract BuildingType BuildingType { get; }

    protected override void Awake()
    {
        base.Awake();
    }
}
