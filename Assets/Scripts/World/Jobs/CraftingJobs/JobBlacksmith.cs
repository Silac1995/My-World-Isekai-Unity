using UnityEngine;

/// <summary>
/// Job de Forgeron : craft des armes et armures dans une ForgeBuilding.
/// Le forgeron est le poste principal du building (souvent le boss).
/// Nécessite une enclume (CraftingStation de type Anvil) pour travailler.
/// </summary>
public class JobBlacksmith : Job
{
    public override string JobTitle => "Forgeron";
    public override JobCategory Category => JobCategory.Artisan;

    private CraftingStation _currentStation;

    /// <summary>
    /// La station de craft actuellement utilisée par le forgeron.
    /// </summary>
    public CraftingStation CurrentStation => _currentStation;

    public override void Execute()
    {
        if (_workplace is ForgeBuilding forge)
        {
            // Trouver une enclume libre si on n'en a pas
            if (_currentStation == null || _currentStation.Occupant != _worker)
            {
                _currentStation = forge.FindAvailableAnvil();
                if (_currentStation == null)
                {
                    Debug.Log($"<color=red>[Job]</color> {_worker.CharacterName} ne trouve pas d'enclume libre à {forge.BuildingName}.");
                    return;
                }
                _currentStation.Use(_worker);
            }

            // TODO: Logique de craft
            Debug.Log($"<color=orange>[Job]</color> {_worker.CharacterName} forge à l'enclume.");
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is ForgeBuilding;
    }

    /// <summary>
    /// Libère la station de craft quand le forgeron arrête de travailler.
    /// </summary>
    public void ReleaseStation()
    {
        if (_currentStation != null)
        {
            _currentStation.Release();
            _currentStation = null;
        }
    }
}
