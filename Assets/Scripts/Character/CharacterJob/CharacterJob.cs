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
}
