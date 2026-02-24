using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MWI.Time;

/// <summary>
/// Composant attaché au personnage pour gérer son emploi du temps.
/// Écoute le TimeManager et applique le bon behaviour à chaque changement d'heure.
/// Les jobs injectent automatiquement leurs créneaux de travail.
/// </summary>
public class CharacterSchedule : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private TimeManager _timeManager;

    [Header("Schedule")]
    [SerializeField] private List<ScheduleEntry> _entries = new List<ScheduleEntry>();

    private ScheduleActivity _currentActivity = ScheduleActivity.Wander;

    public Character Character => _character;
    public TimeManager TimeManager => _timeManager != null ? _timeManager : MWI.Time.TimeManager.Instance;
    public ScheduleActivity CurrentActivity => _currentActivity;
    public IReadOnlyList<ScheduleEntry> Entries => _entries;

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
        if (_timeManager == null) _timeManager = MWI.Time.TimeManager.Instance;
    }

    private void OnEnable()
    {
        var tm = TimeManager;
        if (tm != null)
        {
            tm.OnHourChanged += OnHourChanged;
            Debug.Log($"<color=cyan>[Schedule]</color> {_character?.CharacterName} abonné à OnHourChanged.");
        }
    }

    private void OnDisable()
    {
        var tm = TimeManager;
        if (tm != null)
        {
            tm.OnHourChanged -= OnHourChanged;
        }
    }

    /// <summary>
    /// Appelé par l'event TimeManager.OnHourChanged — une seule fois par heure.
    /// </summary>
    private void OnHourChanged(int newHour)
    {
        Debug.Log($"<color=cyan>[Schedule]</color> {_character?.CharacterName} — OnHourChanged trigger: {newHour}h");
        EvaluateSchedule(newHour);
    }

    /// <summary>
    /// Évalue le schedule à l'heure donnée et applique le behaviour correspondant.
    /// </summary>
    private void EvaluateSchedule(int hour)
    {
        ScheduleActivity newActivity = GetCurrentActivity(hour);

        if (newActivity == _currentActivity) return;

        ScheduleActivity previousActivity = _currentActivity;
        _currentActivity = newActivity;

        Debug.Log($"<color=cyan>[Schedule]</color> {_character.CharacterName} : {previousActivity} → {_currentActivity} (Heure: {hour}h)");

        ApplyActivity(_currentActivity);
    }

    /// <summary>
    /// Détermine quelle activité est active à une heure donnée.
    /// En cas de chevauchement, l'entrée avec la plus haute priorité gagne.
    /// S'il n'y a aucune entrée, retourne Wander par défaut.
    /// </summary>
    public ScheduleActivity GetCurrentActivity(int hour)
    {
        ScheduleEntry bestEntry = null;

        foreach (var entry in _entries)
        {
            if (entry.IsActiveAtHour(hour))
            {
                if (bestEntry == null || entry.priority > bestEntry.priority)
                {
                    bestEntry = entry;
                }
            }
        }

        return bestEntry != null ? bestEntry.activity : ScheduleActivity.Wander;
    }

    /// <summary>
    /// Traduit une ScheduleActivity en IAIBehaviour et l'applique au controller.
    /// </summary>
    private void ApplyActivity(ScheduleActivity activity)
    {
        var npc = _character.Controller as NPCController;
        if (npc == null || !npc.enabled) return;

        IAIBehaviour newBehaviour = activity switch
        {
            ScheduleActivity.Work => new WorkBehaviour(npc),
            ScheduleActivity.Wander => new WanderBehaviour(npc),
            // TODO: Implémenter SleepBehaviour, LeisureBehaviour, GoHomeBehaviour
            ScheduleActivity.Sleep => new WanderBehaviour(npc),
            ScheduleActivity.Leisure => new WanderBehaviour(npc),
            ScheduleActivity.GoHome => new WanderBehaviour(npc),
            _ => new WanderBehaviour(npc)
        };

        npc.ResetStackTo(newBehaviour);
    }

    // ──────────────────────────────────────────────
    //  GESTION DES ENTRÉES
    // ──────────────────────────────────────────────

    /// <summary>
    /// Ajoute une entrée au schedule.
    /// </summary>
    public void AddEntry(ScheduleEntry entry)
    {
        if (entry != null && !_entries.Contains(entry))
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// Retire une entrée du schedule.
    /// </summary>
    public void RemoveEntry(ScheduleEntry entry)
    {
        _entries.Remove(entry);
    }

    /// <summary>
    /// Vide tout le schedule.
    /// </summary>
    public void ClearSchedule()
    {
        _entries.Clear();
        _currentActivity = ScheduleActivity.Wander;
    }

    /// <summary>
    /// Injecte les créneaux de travail d'un job dans le schedule.
    /// </summary>
    public void InjectJobSchedule(List<ScheduleEntry> workEntries)
    {
        if (workEntries == null) return;

        foreach (var entry in workEntries)
        {
            AddEntry(entry);
        }

        Debug.Log($"<color=cyan>[Schedule]</color> {_character.CharacterName} : {workEntries.Count} créneau(x) de travail ajouté(s).");
    }

    /// <summary>
    /// Retire les créneaux de travail du schedule.
    /// </summary>
    public void RemoveJobSchedule(List<ScheduleEntry> workEntries)
    {
        if (workEntries == null) return;

        foreach (var entry in workEntries)
        {
            _entries.Remove(entry);
        }
    }

    /// <summary>
    /// Assigne le TimeManager utilisé par ce schedule.
    /// </summary>
    public void SetTimeManager(TimeManager timeManager)
    {
        // Se désabonner de l'ancien
        if (_timeManager != null)
        {
            _timeManager.OnHourChanged -= OnHourChanged;
        }

        _timeManager = timeManager;

        // S'abonner au nouveau
        if (_timeManager != null && enabled)
        {
            _timeManager.OnHourChanged += OnHourChanged;
        }
    }
}
