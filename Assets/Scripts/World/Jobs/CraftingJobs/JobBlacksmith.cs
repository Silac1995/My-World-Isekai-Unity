using UnityEngine;

/// <summary>
/// Job de Forgeron : craft des armes et armures dans une ForgeBuilding.
/// Le forgeron est le poste principal du building (souvent le boss).
/// </summary>
public class JobBlacksmith : Job
{
    public override string JobTitle => "Forgeron";
    public override JobCategory Category => JobCategory.Artisan;

    public override void Execute()
    {
        if (_workplace is ForgeBuilding forge)
        {
            // TODO: Logique de craft
            // var recipe = forge.GetNextOrder();
            // if (recipe != null) { forger l'objet... }
            Debug.Log($"<color=orange>[Job]</color> {_worker.CharacterName} travaille à la forge.");
        }
    }

    public override bool CanExecute()
    {
        return base.CanExecute() && _workplace is ForgeBuilding;
    }
}
