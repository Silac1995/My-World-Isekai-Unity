using System.Collections.Generic;
using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// Classe mère abstraite pour tous les jobs.
/// Chaque type de job (Barman, Serveur, Vendeur...) hérite de cette classe
/// et override Execute() pour coder sa logique spécifique.
/// </summary>
[System.Serializable]
public abstract class Job
{
    /// <summary>Nom du poste (ex: "Barman", "Serveur")</summary>
    public abstract string JobTitle { get; }

    /// <summary>Catégorie du job (Service, Artisan, etc.)</summary>
    public abstract JobCategory Category { get; }

    /// <summary>V2: Type précis pour la simulation offline (Yields etc)</summary>
    public virtual JobType Type => JobType.None;

    protected Character _worker;
    protected CommercialBuilding _workplace;

    public Character Worker => _worker;
    public CommercialBuilding Workplace => _workplace;
    public bool IsAssigned => _worker != null;
    
    /// <summary>
    /// Utilisé pour le debug. Retourne le nom de l'action ou de l'état en cours (ex: "GOAP: GatherResources").
    /// </summary>
    public virtual string CurrentActionName => string.Empty;

    /// <summary>
    /// Utilisé pour le debug. Retourne le nom de l'objectif GOAP en cours (ex: "Harvest10Wood").
    /// </summary>
    public virtual string CurrentGoalName => string.Empty;

    /// <summary>
    /// Assigne un worker à ce poste dans un building donné.
    /// </summary>
    public virtual void Assign(Character worker, CommercialBuilding workplace)
    {
        if (worker == null || workplace == null) return;

        _worker = worker;
        _workplace = workplace;
        Debug.Log($"<color=yellow>[Job]</color> {worker.CharacterName} assigné comme {JobTitle} à {workplace.BuildingName}.");
    }

    /// <summary>
    /// Retire le worker de ce poste.
    /// </summary>
    public virtual void Unassign()
    {
        if (_worker != null)
        {
            Debug.Log($"<color=yellow>[Job]</color> {_worker.CharacterName} n'est plus {JobTitle} à {_workplace?.BuildingName}.");
        }
        _worker = null;
        _workplace = null;
    }

    /// <summary>
    /// Appelé lors de la fin du service (Punch Out) par le CommercialBuilding.
    /// Utile pour arrêter les coroutines ou nettoyer le GOAP interne du job.
    /// </summary>
    public virtual void OnWorkerPunchOut()
    {
    }

    /// <summary>
    /// Appelé chaque cycle quand le worker est au travail.
    /// Chaque job concret code sa logique spécifique ici.
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// How often (in simulation seconds) <see cref="BTAction_Work.HandleWorking"/>
    /// should call <see cref="Execute"/> for this job. Default is 0.1 s (matches the
    /// BT tick rate, i.e. every BT tick — historical behaviour).
    ///
    /// Heavy-planning jobs (LogisticsManager, Harvester) override to a longer interval
    /// to stagger GOAP planning + dispatcher work, since their per-tick logic is
    /// idempotent on a stable state and the actual reactivity comes from the
    /// underlying GOAP plan / event-driven dispatcher (see
    /// wiki/projects/optimisation-backlog.md entry #2 / Cₐ + B).
    ///
    /// Time-sensitive jobs (Vendor for customer queue, Crafter for animation timing)
    /// keep the default 0.1 s.
    /// </summary>
    public virtual float ExecuteIntervalSeconds => 0.1f;

    /// <summary>
    /// Conditions pour que ce job puisse s'exécuter.
    /// Override dans les sous-classes pour ajouter des conditions spécifiques.
    /// </summary>
    public virtual bool CanExecute()
    {
        return IsAssigned && _worker.IsAlive();
    }

    /// <summary>
    /// Vérifie si le personnage (candidat) possède les prérequis pour prendre ce job.
    /// Override dans les sous-classes (ex: artisans) pour vérifier les compétences.
    /// </summary>
    public virtual bool CanTakeJob(Character applicant)
    {
        return true;
    }

    /// <summary>
    /// Spécifie s'il y a actuellement du travail à faire concrètement pour ce job.
    /// Par exemple, un JobHarvester retourne faux si toutes les ressources sont réunies
    /// et que le reste a été déposé. Utilisé pour donner des pauses aux NPCs.
    /// </summary>
    public virtual bool HasWorkToDo()
    {
        return true;
    }

    /// <summary>
    /// Retourne les créneaux de travail pour ce job.
    /// Chaque sous-classe peut override pour des horaires spécifiques.
    /// Par défaut : 8h → 17h.
    /// </summary>
    public virtual List<ScheduleEntry> GetWorkSchedule()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry(8, 17, ScheduleActivity.Work, 10)
        };
    }
}
