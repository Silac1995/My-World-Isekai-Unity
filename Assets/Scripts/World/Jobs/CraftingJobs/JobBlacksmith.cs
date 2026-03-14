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

    private float _cooldownTimer = 0f;
    private const float CRAFT_COOLDOWN = 2f;

    public override void Execute()
    {
        if (_worker == null) return;

        if (_cooldownTimer > 0f)
        {
            _cooldownTimer -= Time.deltaTime;
            return;
        }

        var npcController = _worker.GetComponent<NPCController>();
        if (npcController != null && !npcController.HasBehaviour<MWI.AI.PerformCraftBehaviour>())
        {
            // Le BT ou le behaviour gérera la recherche de commande et de station
            npcController.PushBehaviour(new MWI.AI.PerformCraftBehaviour(npcController, this, OnCraftFinished));
        }
    }

    private void OnCraftFinished()
    {
        _cooldownTimer = CRAFT_COOLDOWN;
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
