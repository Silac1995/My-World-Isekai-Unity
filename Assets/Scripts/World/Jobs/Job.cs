using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Classe mère abstraite pour tous les jobs.
/// Chaque type de job (Barman, Serveur, Vendeur...) hérite de cette classe
/// et override Execute() pour coder sa logique spécifique.
/// </summary>
public abstract class Job
{
    /// <summary>Nom du poste (ex: "Barman", "Serveur")</summary>
    public abstract string JobTitle { get; }

    /// <summary>Catégorie du job (Service, Artisan, etc.)</summary>
    public abstract JobCategory Category { get; }

    protected Character _worker;
    protected CommercialBuilding _workplace;

    public Character Worker => _worker;
    public CommercialBuilding Workplace => _workplace;
    public bool IsAssigned => _worker != null;

    /// <summary>
    /// Assigne un worker à ce poste dans un building donné.
    /// </summary>
    public void Assign(Character worker, CommercialBuilding workplace)
    {
        if (worker == null || workplace == null) return;

        _worker = worker;
        _workplace = workplace;
        Debug.Log($"<color=yellow>[Job]</color> {worker.CharacterName} assigné comme {JobTitle} à {workplace.BuildingName}.");
    }

    /// <summary>
    /// Retire le worker de ce poste.
    /// </summary>
    public void Unassign()
    {
        if (_worker != null)
        {
            Debug.Log($"<color=yellow>[Job]</color> {_worker.CharacterName} n'est plus {JobTitle} à {_workplace?.BuildingName}.");
        }
        _worker = null;
        _workplace = null;
    }

    /// <summary>
    /// Appelé chaque cycle quand le worker est au travail.
    /// Chaque job concret code sa logique spécifique ici.
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// Conditions pour que ce job puisse s'exécuter.
    /// Override dans les sous-classes pour ajouter des conditions spécifiques.
    /// </summary>
    public virtual bool CanExecute()
    {
        return IsAssigned && _worker.IsAlive();
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
