using UnityEngine;

/// <summary>
/// Interaction physique pour demander un emploi à un patron.
/// Hérite de InteractionInvitation pour simuler le délai de réflexion du boss.
/// </summary>
public class InteractionAskForJob : InteractionInvitation
{
    private CommercialBuilding _building;
    private Job _job;

    public InteractionAskForJob(CommercialBuilding building, Job job)
    {
        _building = building;
        _job = job;
    }

    public override bool CanExecute(Character source, Character target)
    {
        // On ne peut exécuter l'interaction que si le bâtiment a bien un boss et que le job est encore dispo
        return _building != null && _job != null && _building.HasOwner && !_job.IsAssigned;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return $"Bonjour, avez-vous toujours besoin d'un(e) {_job.JobTitle} ?";
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        // Oublie la sociabilité. Seule l'évaluation professionnelle (AskForJob) compte.
        // target (le patron) évalue source (le candidat).
        if (_building.AskForJob(source, _job))
        {
            return true;
        }
        
        return false;
    }

    public override void OnAccepted(Character source, Character target)
    {
        // En cas de succès, on exécute l'engagement officiel !
        if (source.CharacterJob != null)
        {
            if (source.CharacterJob.TakeJob(_job, _building))
            {
                Debug.Log($"<color=green>[InteractionAskForJob]</color> {source.CharacterName} a été physiquement embauché par {target.CharacterName}.");
            }
        }
    }

    public override string GetAcceptMessage()
    {
        return $"Vous êtes embauché ! Bienvenue dans l'équipe.";
    }

    public override string GetRefuseMessage()
    {
        return $"Désolé, mais vous n'avez pas le profil que nous recherchons.";
    }

    public override void OnRefused(Character source, Character target)
    {
        Debug.Log($"<color=orange>[InteractionAskForJob]</color> {source.CharacterName} a été recalé par {target.CharacterName}.");
        // On pourrait ajouter un debuff moral ici plus tard.
    }
}
