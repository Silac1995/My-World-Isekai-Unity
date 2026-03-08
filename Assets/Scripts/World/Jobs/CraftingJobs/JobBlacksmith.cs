using UnityEngine;

/// <summary>
/// Job de Forgeron : craft des armes et armures dans une ForgeBuilding.
/// Le forgeron est le poste principal du building (souvent le boss).
/// Nécessite une enclume (CraftingStation de type Anvil) pour travailler.
/// </summary>
public class JobBlacksmith : JobCrafter
{
    public override string JobTitle => "Forgeron";

    // Un Forgeron a typiquement besoin de "Smithing", mais on le passera dans InitializeJobs ou via AssetDatabase.
    // Pour l'instant, on laisse le constructeur accepter le skill et le tier, avec des valeurs par défaut au besoin.
    public JobBlacksmith(SkillSO smithingSkill, SkillTier tier = SkillTier.Intermediate) : base(smithingSkill, tier)
    {
    }

    private CraftingStation _currentStation;

    /// <summary>
    /// La station de craft actuellement utilisée par le forgeron.
    /// </summary>
    public CraftingStation CurrentStation => _currentStation;

    public override void Execute()
    {
        // L'action réelle est gérée par le BT (PerformCraftBehaviour)
        // Ce Execute de base peut être complété ou laissé vide si le BT fait tout.
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is CraftingBuilding;
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
