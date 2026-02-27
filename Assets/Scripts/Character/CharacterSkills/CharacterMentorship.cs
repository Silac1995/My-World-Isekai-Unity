using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class CharacterMentorship : MonoBehaviour
{
    [SerializeField] private Character _character;
    
    [Header("Student Data")]
    [SerializeField] private Character _currentMentor;
    [SerializeField] private ScriptableObject _learningSubject;
    [SerializeField] private float _learningProgress = 0f; // 0 to 100
    
    [Header("Mentor Data")]
    [SerializeField] private bool _isCurrentlyTeaching = false;
    [SerializeField] private List<MentorshipClass> _hostedClasses = new List<MentorshipClass>();
    [SerializeField] private MentorshipClass _currentActiveClass;
    [SerializeField] private GameObject _mentorClassZonePrefab;
    private MentorClassZone _spawnedClassZone;

    public Character CurrentMentor => _currentMentor;
    public ScriptableObject LearningSubject => _learningSubject;
    public float LearningProgress => _learningProgress;
    public bool IsCurrentlyTeaching { get => _isCurrentlyTeaching; set => _isCurrentlyTeaching = value; }
    public IReadOnlyList<MentorshipClass> HostedClasses => _hostedClasses;
    public MentorshipClass CurrentActiveClass => _currentActiveClass;
    public MentorClassZone SpawnedClassZone => _spawnedClassZone;

    private void Update()
    {
        // --- TEMPORARY AUTO-TEACH TEST ---
        // Si le personnage n'enseigne pas déjà, qu'il est libre, et que son activité actuelle est Wander (temps libre)
        if (!_isCurrentlyTeaching && _character != null && _character.IsFree())
        {
            var schedule = _character.CharacterSchedule;
            // Si on est en Wander, c'est du temps libre. On peut commencer un cours.
            if (schedule != null && schedule.CurrentActivity == ScheduleActivity.Wander)
            {
                // Un Mentor ne commence un cours que s'il a au moins une classe formelle avec des élèves inscrits
                var classToTeach = GetFirstClassWithStudents();
                if (classToTeach != null)
                {
                    Debug.Log($"<color=magenta>[TEST]</color> {_character.CharacterName} a du temps libre, il décide de donner son cours de {classToTeach.TeachingSubject.name} à ses élèves inscrits !");
                    StartGivingLesson(classToTeach.TeachingSubject);
                }
            }
        }
    }

    /// <summary>
    /// Inscrit un élève à l'une des classes gérées par ce mentor. 
    /// Crée la classe si elle n'existe pas encore.
    /// </summary>
    public void EnrollStudentToClass(Character student, ScriptableObject subject)
    {
        var mentorshipClass = _hostedClasses.FirstOrDefault(c => c.TeachingSubject == subject);
        if (mentorshipClass == null)
        {
            mentorshipClass = new MentorshipClass(_character, subject);
            _hostedClasses.Add(mentorshipClass);
        }

        if (mentorshipClass.EnrollStudent(student))
        {
            Debug.Log($"<color=cyan>[Mentorship]</color> {student.CharacterName} s'est officiellement inscrit au cours de {subject.name} de {_character.CharacterName}.");
        }
    }

    /// <summary>
    /// Retire un élève de l'enseignement formel.
    /// </summary>
    public void RemoveStudentFromClass(Character student, ScriptableObject subject)
    {
        var mentorshipClass = _hostedClasses.FirstOrDefault(c => c.TeachingSubject == subject);
        if (mentorshipClass != null)
        {
            mentorshipClass.RemoveStudent(student);
        }
    }

    private MentorshipClass GetFirstClassWithStudents()
    {
        return _hostedClasses.FirstOrDefault(c => c.HasStudents());
    }

    /// <summary>
    /// Assigne un mentor à ce personnage pour un sujet précis.
    /// </summary>
    public void SetMentor(Character mentor, ScriptableObject subject)
    {
        _currentMentor = mentor;
        _learningSubject = subject;
        _learningProgress = 0f;
    }

    public void ClearMentor()
    {
        _currentMentor = null;
        _learningSubject = null;
        _learningProgress = 0f;
    }

    /// <summary>
    /// Démarre l'action d'enseigner. (Peut être appelé par le BT, ou via un script externe).
    /// </summary>
    public void StartGivingLesson(ScriptableObject subjectToTeach)
    {
        if (_isCurrentlyTeaching) return;
        
        var targetClass = _hostedClasses.FirstOrDefault(c => c.TeachingSubject == subjectToTeach);
        if (targetClass == null || !targetClass.HasStudents())
        {
            Debug.LogWarning($"<color=orange>[Mentorship]</color> {_character.CharacterName} a essayé de donner un cours de {subjectToTeach.name}, mais il n'a aucun élève d'inscrit.");
            return;
        }

        _isCurrentlyTeaching = true;
        _currentActiveClass = targetClass;

        // Spawn la zone aux pieds du mentor
        if (_mentorClassZonePrefab != null && _spawnedClassZone == null)
        {
            var zoneObj = Instantiate(_mentorClassZonePrefab, transform.position, Quaternion.identity);
            _spawnedClassZone = zoneObj.GetComponent<MentorClassZone>();
            if (_spawnedClassZone != null)
            {
                _spawnedClassZone.InitializeClass(targetClass);
                _spawnedClassZone.StartClass();
            }
        }

        // Force le NPC à se diriger vers sa MentorClassZone
        var npcController = _character.Controller as NPCController;
        if (npcController != null && !npcController.HasBehaviourTree)
        {
            npcController.SetBehaviour(new GiveLessonBehaviour());
        }

        Debug.Log($"<color=green>[Mentorship]</color> {_character.CharacterName} commence à donner son cours de {subjectToTeach?.name}.");
    }

    /// <summary>
    /// Arrête l'action d'enseigner.
    /// </summary>
    public void StopGivingLesson()
    {
        if (!_isCurrentlyTeaching) return;
        _isCurrentlyTeaching = false;
        _currentActiveClass = null;

        if (_spawnedClassZone != null)
        {
            _spawnedClassZone.EndClass();
            Destroy(_spawnedClassZone.gameObject);
            _spawnedClassZone = null;
        }

        var npcController = _character.Controller as NPCController;
        if (npcController != null && !npcController.HasBehaviourTree)
        {
            var currentGive = npcController.GetCurrentBehaviour<GiveLessonBehaviour>();
            if (currentGive != null) currentGive.Terminate();
        }

        Debug.Log($"<color=green>[Mentorship]</color> {_character.CharacterName} a fini de donner son cours.");
    }

    /// <summary>
    /// Reçoit un tick d'XP (ou de progression d'apprentissage) pendant un cours donné par un mentor.
    /// </summary>
    public void ReceiveLessonTick(ScriptableObject subject, SkillTier mentorTier, float baseXP)
    {
        if (subject == null) return;
        
        float finalXP = baseXP * mentorTier.GetMentorshipXPMultiplier();

        if (subject is SkillSO skillSO)
        {
            CharacterSkills skills = _character.CharacterSkills;
            if (skills != null && skills.HasSkill(skillSO))
            {
                // Vérifier la limite de Tier
                SkillTier studentTier = SkillTierExtensions.GetTierForLevel(skills.GetSkillLevel(skillSO));
                if ((int)studentTier >= (int)mentorTier - 1)
                {
                    // L'élève a atteint la limite d'enseignement pour ce maître
                    return;
                }
                skills.GainXP(skillSO, Mathf.CeilToInt(finalXP));
            }
            else
            {
                // Progresse l'apprentissage
                _learningProgress += finalXP; 
                if (_learningProgress >= 100f)
                {
                    skills.AddSkill(skillSO, 1);
                    _learningProgress = 0f;
                    Debug.Log($"<color=cyan>[Mentorship]</color> {_character.CharacterName} a enfin appris la compétence {skillSO.SkillName} !");
                }
            }
        }
        else if (subject is CombatStyleSO combatSO)
        {
            CharacterCombat combat = _character.CharacterCombat;
            if (combat == null) return;
            var expertise = combat.KnownStyles.FirstOrDefault(s => s.Style == combatSO);
            if (expertise != null)
            {
                SkillTier studentTier = expertise.CurrentTier;
                if ((int)studentTier >= (int)mentorTier - 1)
                {
                    // L'élève a atteint la limite d'enseignement pour ce maître
                    return;
                }
                expertise.AddExperience(finalXP);
            }
            else
            {
                // Progresse l'apprentissage
                _learningProgress += finalXP;
                if (_learningProgress >= 100f)
                {
                    combat.UnlockCombatStyle(combatSO);
                    _learningProgress = 0f;
                    Debug.Log($"<color=cyan>[Mentorship]</color> {_character.CharacterName} a enfin appris le style {combatSO.StyleName} !");
                }
            }
        }
    }

    /// <summary>
    /// Retourne la liste des sujets que ce personnage a le droit d'enseigner (Niveau >= 35 / Advanced).
    /// </summary>
    public List<ScriptableObject> GetTeachableSubjects()
    {
        List<ScriptableObject> subjects = new List<ScriptableObject>();

        // 1. Chercher les Skills
        CharacterSkills skills = _character.CharacterSkills;
        if (skills != null)
        {
            foreach (var instance in skills.Skills)
            {
                if (instance != null && instance.Level >= 35) // 35 = Advanced
                {
                    Debug.Log($"<color=magenta>[TEST]</color> Trouvé Skill {instance.Skill.SkillName} lvl {instance.Level}");
                    subjects.Add(instance.Skill);
                }
            }
        }

        // 2. Chercher les Styles de Combat
        CharacterCombat combat = _character.CharacterCombat;
        if (combat != null)
        {
            foreach (var expertise in combat.KnownStyles)
            {
                if (expertise != null && expertise.Level >= 35)
                {
                    subjects.Add(expertise.Style);
                }
            }
        }

        return subjects;
    }
}
