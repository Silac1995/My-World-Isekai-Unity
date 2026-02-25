using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Composant attaché au personnage pour gérer son job actuel.
/// Permet d'assigner, quitter, et exécuter un job dans un CommercialBuilding.
/// </summary>
public class CharacterJob : MonoBehaviour
{
    [SerializeField] private Character _character;

    private Job _currentJob;
    private CommercialBuilding _workplace;
    private List<ScheduleEntry> _workScheduleEntries;

    public Character Character => _character;
    public Job CurrentJob => _currentJob;
    public CommercialBuilding Workplace => _workplace;
    public bool HasJob => _currentJob != null;
    public bool IsWorking => HasJob && _currentJob.IsAssigned;

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    /// <summary>
    /// Assigne un job au personnage dans un building commercial.
    /// </summary>
    public bool TakeJob(Job job, CommercialBuilding building)
    {
        if (job == null || building == null) return false;

        // Quitter le job actuel si on en a un
        if (HasJob)
        {
            QuitJob();
        }

        // Assigner le nouveau job
        if (building.AssignWorker(_character, job))
        {
            _currentJob = job;
            _workplace = building;

            // Injecter les horaires de travail dans le schedule
            InjectWorkSchedule();

            Debug.Log($"<color=yellow>[CharacterJob]</color> {_character.CharacterName} a pris le poste de {job.JobTitle} à {building.BuildingName}.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Le personnage quitte son job actuel.
    /// </summary>
    public void QuitJob()
    {
        if (!HasJob) return;

        Debug.Log($"<color=yellow>[CharacterJob]</color> {_character.CharacterName} quitte son poste de {_currentJob.JobTitle} à {_workplace?.BuildingName}.");

        // Retirer les horaires de travail du schedule
        RemoveWorkSchedule();

        if (_workplace != null)
        {
            _workplace.RemoveWorker(_currentJob);
        }

        _currentJob = null;
        _workplace = null;
    }

    private void InjectWorkSchedule()
    {
        if (_currentJob == null) return;

        var schedule = _character.CharacterSchedule;
        if (schedule == null) return;

        _workScheduleEntries = _currentJob.GetWorkSchedule();
        schedule.InjectJobSchedule(_workScheduleEntries);
    }

    private void RemoveWorkSchedule()
    {
        var schedule = _character.CharacterSchedule;
        if (schedule == null || _workScheduleEntries == null) return;

        schedule.RemoveJobSchedule(_workScheduleEntries);
        _workScheduleEntries = null;
    }

    /// <summary>
    /// Exécute le travail du job actuel (appelé quand le personnage est au travail).
    /// </summary>
    public void Work()
    {
        if (!HasJob) return;

        if (_currentJob.CanExecute())
        {
            _currentJob.Execute();
        }
    }

    /// <summary>
    /// Vérifie si le personnage travaille dans un building spécifique.
    /// </summary>
    public bool WorksAt(CommercialBuilding building)
    {
        return _workplace != null && _workplace == building;
    }

    /// <summary>
    /// Vérifie si le personnage a un job d'un type spécifique.
    /// </summary>
    public bool HasJobOfType<T>() where T : Job
    {
        return _currentJob is T;
    }

    /// <summary>
    /// Le personnage est-il propriétaire de son lieu de travail ?
    /// </summary>
    public bool IsOwner => _workplace != null && _workplace.Owner == _character;

    /// <summary>
    /// Le personnage demande un job dans un building commercial.
    /// Passe par le building qui vérifie que le boss existe.
    /// </summary>
    public bool AskForJob(CommercialBuilding building, Job job)
    {
        if (building == null || job == null) return false;

        // Demande au building (qui check le boss)
        if (building.AskForJob(_character, job))
        {
            // Le building a accepté → on enregistre le job côté personnage
            if (HasJob) QuitJob();

            _currentJob = job;
            _workplace = building;
            InjectWorkSchedule();

            Debug.Log($"<color=yellow>[CharacterJob]</color> {_character.CharacterName} a été embauché comme {job.JobTitle} à {building.BuildingName}.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Le personnage devient propriétaire d'un building commercial.
    /// Il peut optionnellement prendre un job dans ce building.
    /// </summary>
    public bool BecomeOwner(CommercialBuilding building, Job optionalJob = null)
    {
        if (building == null) return false;

        // Quitter le job actuel si on en a un
        if (HasJob) QuitJob();

        // Devenir propriétaire (SetOwner assigne aussi le job si fourni)
        building.SetOwner(_character, optionalJob);

        // Si un job a été assigné, enregistrer les infos côté personnage
        if (optionalJob != null && optionalJob.IsAssigned && optionalJob.Worker == _character)
        {
            _currentJob = optionalJob;
            _workplace = building;
            InjectWorkSchedule();
        }
        else
        {
            // Juste owner, pas de job
            _workplace = building;
        }

        Debug.Log($"<color=green>[CharacterJob]</color> {_character.CharacterName} est devenu propriétaire de {building.BuildingName}.");
        return true;
    }
}
