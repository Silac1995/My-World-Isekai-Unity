using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(NavMeshModifierVolume))]
public class MentorClassZone : MonoBehaviour
{
    [Header("Class Definition")]
    public Character Mentor;
    public ScriptableObject TeachingSkill; // Supporte SkillSO ou CombatStyleSO
    public List<Character> ActiveStudents = new List<Character>();

    [Header("Class Schedule")]
    [Tooltip("Les plages horaires durant lesquelles la classe est ouverte.")]
    public List<ScheduleEntry> ClassSchedules = new List<ScheduleEntry>();
    
    public bool IsClassActive { get; private set; }

    [SerializeField] private BoxCollider _zoneCollider;
    [SerializeField] private NavMeshModifierVolume _navMeshModifier;

    private void Awake()
    {
        _zoneCollider = GetComponent<BoxCollider>();
        _zoneCollider.isTrigger = true;

        _navMeshModifier = GetComponent<NavMeshModifierVolume>();
    }

    private void Update()
    {
        CheckSchedule();
    }

    private void CheckSchedule()
    {
        if (ClassSchedules == null || ClassSchedules.Count == 0) return;

        int currentHour = MWI.Time.TimeManager.Instance.CurrentHour;
        bool shouldBeActive = false;

        foreach (var schedule in ClassSchedules)
        {
            if (schedule.IsActiveAtHour(currentHour))
            {
                shouldBeActive = true;
                break;
            }
        }

        if (shouldBeActive && !IsClassActive)
        {
            StartClass();
        }
        else if (!shouldBeActive && IsClassActive)
        {
            EndClass();
        }
    }

    public void StartClass()
    {
        if (IsClassActive) return;
        IsClassActive = true;
        Debug.Log($"<color=yellow>[Class]</color> La classe de {TeachingSkill?.name} animée par {Mentor?.CharacterName} vient de commencer !");
    }

    public void EndClass()
    {
        if (!IsClassActive) return;
        IsClassActive = false;
        
        // Optionnel: Vider la liste d'élèves quand la classe ferme
        // ActiveStudents.Clear();
        
        Debug.Log($"<color=yellow>[Class]</color> La classe de {TeachingSkill?.name} animée par {Mentor?.CharacterName} est terminée.");
    }

    /// <summary>
    /// Initialise la zone d'enseignement simplifiée pour les préfabs déjà paramétrés.
    /// </summary>
    public void InitializeClass(Character mentor, ScriptableObject skill)
    {
        Mentor = mentor;
        TeachingSkill = skill;
        ActiveStudents.Clear();
    }

    /// <summary>
    /// Initialise la zone d'enseignement.
    /// </summary>
    public void Initialize(Character mentor, ScriptableObject skill, Vector3 zoneSize, int navMeshAreaId)
    {
        Mentor = mentor;
        TeachingSkill = skill;
        ActiveStudents.Clear();

        _zoneCollider.size = zoneSize;
        _navMeshModifier.size = zoneSize;
        _navMeshModifier.center = _zoneCollider.center;
        _navMeshModifier.area = navMeshAreaId;
        
        gameObject.tag = "MentorZone"; // Optionnel, pour d'autres traitements
    }

    // Gestion de la liste des élèves présents physiquement dans la zone
    private void OnTriggerEnter(Collider other)
    {
        Character chara = other.GetComponent<Character>();
        if (chara != null && chara != Mentor)
        {
            if (!ActiveStudents.Contains(chara))
            {
                ActiveStudents.Add(chara);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        Character chara = other.GetComponent<Character>();
        if (chara != null && ActiveStudents.Contains(chara))
        {
            ActiveStudents.Remove(chara);
        }
    }
}
