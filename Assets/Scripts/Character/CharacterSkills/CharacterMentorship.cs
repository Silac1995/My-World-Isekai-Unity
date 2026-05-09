using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using MWI.Time;

public class CharacterMentorship : CharacterSystem, IInteractionProvider
{
    
    [Header("Student Data")]
    [SerializeField] private Character _currentMentor;
    [SerializeField] private ScriptableObject _learningSubject;
    [SerializeField] private float _learningProgress = 0f; // 0 to 100

    // Server-authoritative sync of the mentor's NetworkObjectId so remote clients can
    // resolve CurrentMentor without relying on the serialized _currentMentor field
    // (which is only mutated on the server inside SetMentor / ClearMentor).
    public NetworkVariable<ulong> CurrentMentorNetId = new NetworkVariable<ulong>(
        0UL,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Header("Mentor Data")]
    [SerializeField] private bool _isCurrentlyTeaching = false;
    [SerializeField] private List<MentorshipClass> _hostedClasses = new List<MentorshipClass>();
    [SerializeField] private MentorshipClass _currentActiveClass;
    [SerializeField] private GameObject _mentorClassZonePrefab;
    private MentorClassZone _spawnedClassZone;

    public Character CurrentMentor
    {
        get
        {
            // Server / host keeps the authoritative reference in _currentMentor.
            if (_currentMentor != null) return _currentMentor;

            // Remote clients resolve via the replicated NetworkObjectId.
            ulong id = CurrentMentorNetId.Value;
            if (id == 0) return null;
            if (NetworkManager.Singleton == null) return null;
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj) && obj != null)
                return obj.GetComponent<Character>();
            return null;
        }
    }
    public ScriptableObject LearningSubject => _learningSubject;
    public float LearningProgress => _learningProgress;
    public bool IsCurrentlyTeaching { get => _isCurrentlyTeaching; set => _isCurrentlyTeaching = value; }
    public IReadOnlyList<MentorshipClass> HostedClasses => _hostedClasses;
    public MentorshipClass CurrentActiveClass => _currentActiveClass;
    public MentorClassZone SpawnedClassZone => _spawnedClassZone;

    private ScheduleEntry _dynamicClassEntry;

    protected override void OnEnable()
    {
        base.OnEnable();

        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnHourChanged += OnHourChanged;
        }

        if (_character != null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnCombatModeChanged += HandleCombatModeChanged;
        }
    }

    protected override void OnDisable()
    {
        if (TimeManager.Instance != null)
        {
            TimeManager.Instance.OnHourChanged -= OnHourChanged;
        }

        if (_character != null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnCombatModeChanged -= HandleCombatModeChanged;
        }

        base.OnDisable();
    }

    private void HandleCombatModeChanged(bool isInCombat)
    {
        // Si le mentor entre en combat (se fait attaquer ou attaque), on annule la leçon
        if (isInCombat && _isCurrentlyTeaching)
        {
            Debug.Log($"<color=orange>[Mentorship]</color> Le cours de {_character.CharacterName} est interrompu par un combat !");
            
            // On s'assure de dire une phrase RP pour l'interruption
            if (_character.CharacterSpeech != null)
            {
                _character.CharacterSpeech.Say("We are under attack! Class dismissed!");
            }

            StopGivingLesson();
        }
    }

    private void OnHourChanged(int hour)
    {
        // 1. Nettoyer l'ancienne classe dynamique si elle est finie
        if (_dynamicClassEntry != null && !_dynamicClassEntry.IsActiveAtHour(hour))
        {
            var schedule = _character.CharacterSchedule;
            if (schedule != null) schedule.RemoveEntry(_dynamicClassEntry);
            _dynamicClassEntry = null;
            
            // Si on donnait cours, on arrête
            if (_isCurrentlyTeaching) StopGivingLesson();
        }

        // 2. Si on ne donne pas encours de cours, on regarde si on a des élèves
        if (!_isCurrentlyTeaching && _character.IsFree())
        {
            var schedule = _character.CharacterSchedule;
            if (schedule != null)
            {
                // Uniquement si on a défini de se balader (Wander) pendant cette heure
                if (schedule.GetCurrentActivity(hour) == ScheduleActivity.Wander || schedule.GetCurrentActivity(hour) == ScheduleActivity.Teach)
                {
                    var classToTeach = GetFirstClassWithStudents();
                    if (classToTeach != null)
                    {
                        // On planifie un cours pour cette heure-ci avec une haute priorité
                        _dynamicClassEntry = new ScheduleEntry(hour, hour + 1, ScheduleActivity.Teach, 50);
                        schedule.AddEntry(_dynamicClassEntry);
                        schedule.ReevaluateCurrentActivity(); // Force l'application immédiate
                        
                        // L'appel précédent va declencher GiveLessonBehaviour, 
                        // mais on s'assure d'initialiser les données de la classe.
                        StartGivingLesson(classToTeach.TeachingSubject);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Appelé lors de l'ajout d'un nouvel élève. Tente de lancer le cours immédiatement si le mentor est libre.
    /// </summary>
    public void TryScheduleImmediateClass(MentorshipClass classToTeach)
    {
        if (_isCurrentlyTeaching) return; // Déjà en train de donner cours
        if (!_character.IsFree()) return;

        if (TimeManager.Instance == null) return;
        int currentHour = TimeManager.Instance.CurrentHour;

        var schedule = _character.CharacterSchedule;
        if (schedule != null)
        {
            if (schedule.GetCurrentActivity(currentHour) == ScheduleActivity.Wander || schedule.GetCurrentActivity(currentHour) == ScheduleActivity.Teach)
            {
                // Nettoyer si existant
                if (_dynamicClassEntry != null) schedule.RemoveEntry(_dynamicClassEntry);

                // Planifier le cours immédiat pour l'heure en cours
                _dynamicClassEntry = new ScheduleEntry(currentHour, currentHour + 1, ScheduleActivity.Teach, 50);
                schedule.AddEntry(_dynamicClassEntry);
                schedule.ReevaluateCurrentActivity();

                StartGivingLesson(classToTeach.TeachingSubject);
            }
        }
    }

    /// <summary>
    /// Inscrit un élève à l'une des classes gérées par ce mentor. 
    /// Crée la classe si elle n'existe pas encore.
    /// Retourne la classe pour que l'élève puisse s'y abonner.
    /// </summary>
    public MentorshipClass EnrollStudentToClass(Character student, ScriptableObject subject)
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

        return mentorshipClass;
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
    /// Assigne un mentor à ce personnage pour un sujet précis, et s'abonne aux événements de classe.
    /// </summary>
    public void SetMentor(Character mentor, ScriptableObject subject, MentorshipClass targetClass)
    {
        ClearMentor(); // S'assurer de nettoyer les anciens events

        _currentMentor = mentor;
        _learningSubject = subject;
        _learningProgress = 0f;

        // Replicate the mentor's net-id so remote clients (notably the student's own
        // owning client when the student is a remote player) can see they now have a
        // mentor — otherwise hold-E menus on other mentors won't gray out the entries.
        if (IsServer)
        {
            CurrentMentorNetId.Value = (mentor != null && mentor.NetworkObject != null)
                ? mentor.NetworkObject.NetworkObjectId
                : 0UL;
        }

        if (targetClass != null)
        {
            targetClass.OnClassStarted -= HandleMentorClassStarted;
            targetClass.OnClassStarted += HandleMentorClassStarted;
        }
    }

    public void ClearMentor()
    {
        if (_currentMentor != null)
        {
            var mentorMentorship = _currentMentor.CharacterMentorship;
            if (mentorMentorship != null)
            {
                var mentorshipClass = mentorMentorship.HostedClasses.FirstOrDefault(c => c.TeachingSubject == _learningSubject);
                if (mentorshipClass != null)
                {
                    mentorshipClass.OnClassStarted -= HandleMentorClassStarted;
                }
            }
        }

        _currentMentor = null;
        _learningSubject = null;
        _learningProgress = 0f;

        // Clear the replicated id so remote clients see the student become mentor-less
        // (enables menu entries on other mentors to un-gray).
        if (IsServer)
        {
            CurrentMentorNetId.Value = 0UL;
        }
    }

    private void HandleMentorClassStarted(MentorshipClass activeClass)
    {
        // On vérifie qu'on est au bon endroit et libre de nous y rendre
        if (!_character.IsFree()) return;

        var schedule = _character.CharacterSchedule;
        if (schedule != null && schedule.CurrentActivity == ScheduleActivity.Wander)
        {
            var npcController = _character.Controller as NPCController;
            if (npcController != null && !npcController.HasBehaviourTree)
            {
                var currentBehaviour = npcController.GetCurrentBehaviour<AttendClassBehaviour>();
                if (currentBehaviour == null)
                {
                    Debug.Log($"<color=magenta>[TEST-EVENT]</color> {_character.CharacterName} a reçu la notification que le cours a commencé. Il s'y rend !");
                    npcController.SetBehaviour(new AttendClassBehaviour(npcController));
                }
            }
        }
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

        // Spawn la zone à un emplacement valide (N/S/E/W) du mentor
        if (_mentorClassZonePrefab != null && _spawnedClassZone == null)
        {
            // On estime grossièrement la taille de la zone nécessaire pour trouver un emplacement
            int totalPeople = targetClass.EnrolledStudents.Count + 1;
            int gridSide = Mathf.CeilToInt(Mathf.Sqrt(totalPeople));
            float estimatedSize = (gridSide * 5f) + 2f;

            Vector3 spawnPos = FindValidClassPosition(estimatedSize);
            
            var zoneObj = Instantiate(_mentorClassZonePrefab, spawnPos, Quaternion.identity);
            _spawnedClassZone = zoneObj.GetComponent<MentorClassZone>();
            
            // Faire tourner la zone pour qu'elle "regarde" le mentor (utile si des props visuels sont dans la zone)
            Vector3 directionToMentor = transform.position - spawnPos;
            directionToMentor.y = 0;
            if (directionToMentor.sqrMagnitude > 0.1f)
            {
                zoneObj.transform.rotation = Quaternion.LookRotation(directionToMentor);
            }

            if (_spawnedClassZone != null)
            {
                _spawnedClassZone.InitializeClass(targetClass, _dynamicClassEntry);
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
    /// Cherche un emplacement valide (N/S/E/W) proche du mentor pour instancier la zone d'enseignement.
    /// Renvoie la position du mentor si aucune place n'est trouvée.
    /// </summary>
    private Vector3 FindValidClassPosition(float zoneSize)
    {
        // La distance à laquelle on veut placer le centre de la zone (moitié de la zone + espace pour le mentor)
        float targetDistance = (zoneSize / 2f) + 2f; 
        
        Vector3[] directions = new Vector3[]
        {
            transform.forward,  // Devant
            transform.right,    // Droite
            -transform.right,   // Gauche
            -transform.forward  // Derrière
        };

        foreach (var dir in directions)
        {
            // Position théorique du centre de la zone
            Vector3 testPos = transform.position + (dir * targetDistance);
            
            // 1. On vérifie d'abord qu'on peut tracer une ligne droite sur le NavMesh jusqu'à cet endroit
            // (Si le raycast touche quelque chose, c'est qu'il y a un mur ou le bord de la map)
            bool hitWall = UnityEngine.AI.NavMesh.Raycast(transform.position, testPos, out UnityEngine.AI.NavMeshHit edgeHit, UnityEngine.AI.NavMesh.AllAreas);
            
            if (!hitWall)
            {
                // Pas de mur ! On valide en s'assurant que le point est bien sur le NavMesh
                if (UnityEngine.AI.NavMesh.SamplePosition(testPos, out UnityEngine.AI.NavMeshHit validHit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    return validHit.position;
                }
            }
            else
            {
                // Si ça a touché un mur (bord de map), on regarde si la distance parcourue avant le mur
                // est suffisante pour y caler notre classe quand même.
                if (edgeHit.distance > (zoneSize / 2f) + 1f)
                {
                    // L'espace est tronqué, mais suffisant. On recule un peu par rapport au mur
                    Vector3 adjustedPos = transform.position + (dir * (edgeHit.distance - (zoneSize / 2f)));
                    if (UnityEngine.AI.NavMesh.SamplePosition(adjustedPos, out UnityEngine.AI.NavMeshHit validHit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        return validHit.position;
                    }
                }
            }
        }

        // Si vraiment coincé de tous les côtés, on spawn à nos pieds
        Debug.LogWarning($"<color=orange>[Mentorship]</color> Espace trop restreint pour le cours ! Spawn du ClassZone à la position exacte du Mentor.");
        return transform.position;
    }

    /// <summary>
    /// Arrête l'action d'enseigner.
    /// </summary>
    public void StopGivingLesson()
    {
        if (!_isCurrentlyTeaching) return;
        _isCurrentlyTeaching = false;
        _currentActiveClass = null;

        if (_dynamicClassEntry != null)
        {
            var schedule = _character.CharacterSchedule;
            if (schedule != null)
            {
                schedule.RemoveEntry(_dynamicClassEntry);
                schedule.ReevaluateCurrentActivity();
            }
            _dynamicClassEntry = null;
        }

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
                    Graduate(skillSO);
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
                    Graduate(combatSO);
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
        else if (subject is AbilitySO abilitySO)
        {
            CharacterAbilities abilities = _character.CharacterAbilities;
            if (abilities == null) return;

            if (abilities.KnowsAbility(abilitySO))
            {
                Graduate(abilitySO);
                return;
            }

            _learningProgress += finalXP;
            if (_learningProgress >= 100f)
            {
                abilities.LearnAbility(abilitySO);
                _learningProgress = 0f;
                Debug.Log($"<color=cyan>[Mentorship]</color> {_character.CharacterName} learned ability: {abilitySO.AbilityName}!");
            }
        }
    }

    /// <summary>
    /// Appelé quand l'élève a complété son apprentissage (A atteint le maximum possible sous ce mentor).
    /// </summary>
    private void Graduate(ScriptableObject subject)
    {
        Debug.Log($"<color=green>[Mentorship - Graduation]</color> {_character.CharacterName} n'a plus rien à apprendre de son maître pour {subject.name}. L'enseignement est terminé !");

        if (_currentMentor != null)
        {
            var mentorMentorship = _currentMentor.CharacterMentorship;
            if (mentorMentorship != null)
            {
                // Demander au maître de le retirer de sa classe
                mentorMentorship.RemoveStudentFromClass(_character, subject);
            }
        }
        
        // Dire une petite phrase et se nettoyer
        if (_character.CharacterSpeech != null)
            _character.CharacterSpeech.Say("I've learned all I can! Thank you, master!");

        ClearMentor();
        
        // Stopper le behaviour si on est en train de suivre le cours
        var npcController = _character.Controller as NPCController;
        if (npcController != null)
        {
            var currentGive = npcController.GetCurrentBehaviour<AttendClassBehaviour>();
            if (currentGive != null) currentGive.Terminate();
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

        // 3. Known Abilities (all known abilities are teachable)
        CharacterAbilities charAbilities = _character.CharacterAbilities;
        if (charAbilities != null)
        {
            foreach (var ability in charAbilities.AllKnownAbilities())
            {
                if (ability?.Data != null)
                    subjects.Add(ability.Data);
            }
        }

        return subjects;
    }

    /// <summary>
    /// Returns true if <paramref name="student"/> can still learn <paramref name="subject"/>
    /// from this mentor. A student can learn only while their tier in the subject is strictly
    /// below <c>mentorTier - 1</c> (e.g. a Master mentor can teach up to Advanced-1, i.e. Intermediate).
    /// If the student does not know the subject at all, they can always learn it.
    /// </summary>
    public bool CanTeachStudent(Character student, ScriptableObject subject)
    {
        if (student == null || subject == null)
        {
            Debug.LogWarning($"[Mentorship] CanTeachStudent called with null argument on {_character?.CharacterName} (student={student}, subject={subject})");
            return false;
        }

        // Logic preserved from InteractionMentorship.CanStudentStillLearn with subsystem null-guards
        // matching the pattern in GetTeachableSubjects() above and ReceiveLessonTick() elsewhere.

        // 1. Mentor tier for this subject
        SkillTier mentorTier = SkillTier.Novice;
        if (subject is SkillSO skill)
        {
            CharacterSkills skills = _character.CharacterSkills;
            if (skills != null)
                mentorTier = SkillTierExtensions.GetTierForLevel(skills.GetSkillLevel(skill));
        }
        else if (subject is CombatStyleSO style)
        {
            CharacterCombat combat = _character.CharacterCombat;
            if (combat != null)
            {
                var expertise = combat.KnownStyles.FirstOrDefault(s => s.Style == style);
                if (expertise != null) mentorTier = expertise.CurrentTier;
            }
        }
        // AbilitySO and any other non-handled type: mentorTier stays Novice.
        // The tier gate at the bottom then evaluates `Novice < Novice - 1` = false,
        // which matches the pre-refactor behaviour for abilities.

        // 2. Student tier for this subject
        SkillTier studentTier = SkillTier.Novice;
        if (subject is SkillSO studentSkill)
        {
            CharacterSkills studentSkills = student.CharacterSkills;
            if (studentSkills == null || !studentSkills.HasSkill(studentSkill))
                return true; // Student doesn't know this skill at all, so they can always start learning
            studentTier = SkillTierExtensions.GetTierForLevel(studentSkills.GetSkillLevel(studentSkill));
        }
        else if (subject is CombatStyleSO studentStyle)
        {
            CharacterCombat studentCombat = student.CharacterCombat;
            if (studentCombat == null)
                return true; // Student doesn't know this style at all, so they can always start learning
            var expertise = studentCombat.KnownStyles.FirstOrDefault(s => s.Style == studentStyle);
            if (expertise != null)
                studentTier = expertise.CurrentTier;
            else
                return true; // Student doesn't know this style at all, so they can always start learning
        }

        // 3. Gate: student must be strictly below mentorTier - 1
        return (int)studentTier < (int)mentorTier - 1;
    }

    /// <summary>
    /// Calcule le pourcentage de chance (0 à 100) que ce mentor accepte de prendre le personnage en tant qu'élève.
    /// Basé principalement sur la relation, le charisme, et potentiellement le nombre d'élèves actuels.
    /// </summary>
    public float CalculateAcceptanceChance(Character student)
    {
        float baseChance = 0f;

        // 1. Facteur de Relation (Élément principal)
        if (_character.CharacterRelation != null)
        {
            var rel = _character.CharacterRelation.GetRelationshipWith(student);
            
            // Si on ne se connaît pas, ou si on se déteste, aucune chance.
            if (rel == null || rel.RelationValue < 0) return 0f;

            // Relation de 0 à 100 donne 0% à 70% de chance d'acceptation de base
            baseChance += (rel.RelationValue / 100f) * 70f;
        }

        // 2. Bonus de Charisme de l'élève (Jusqu'à +20%)
        if (student.Stats != null && student.Stats.Charisma != null)
        {
            // Charisme de 0 à 100 donne 0 à +20%
            float charismaBonus = (student.Stats.Charisma.Value / 100f) * 20f;
            baseChance += charismaBonus;
        }

        // 3. Malus d'engorgement : Moins de chance si le maître a déjà beaucoup d'élèves
        int totalStudents = _hostedClasses.Sum(c => c.EnrolledStudents.Count);
        if (totalStudents > 0)
        {
            // Par exemple, -10% par élève déjà inscrit.
            baseChance -= (totalStudents * 10f);
        }

        // 4. Bonus de base (10% de grâce minimum si relation >= 0)
        baseChance += 10f;

        return Mathf.Clamp(baseChance, 0f, 100f);
    }

    // ─────────────────────────────────────────────────────────────
    //  IInteractionProvider — hold-E menu entries ("Ask to teach X")
    // ─────────────────────────────────────────────────────────────

    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        if (interactor == null || interactor == _character) return null;
        if (interactor.CharacterMentorship == null) return null; // source can't learn anyway

        var teachable = GetTeachableSubjects();
        if (teachable == null || teachable.Count == 0) return null;

        var options = new List<InteractionOption>(teachable.Count);
        foreach (var subject in teachable)
        {
            if (subject == null) continue;

            // Already the student of THIS mentor in THIS subject → hide entry entirely
            if (interactor.CharacterMentorship.CurrentMentor == _character &&
                interactor.CharacterMentorship.LearningSubject == subject)
                continue;

            bool disabled = false;
            string reason = null;

            // Primary check: resolved CurrentMentor (works on server and when the mentor's
            // NetworkObject is live on the client). Secondary check: CurrentMentorNetId != 0
            // covers the edge case where the student's mentor is currently hibernated /
            // NetworkObject-despawned — SpawnedObjects lookup fails but the student IS
            // still enrolled server-side, and CanExecute would silently reject the click.
            bool hasActiveMentor = interactor.CharacterMentorship.CurrentMentor != null
                                || interactor.CharacterMentorship.CurrentMentorNetId.Value != 0UL;

            if (hasActiveMentor)
            {
                disabled = true;
                reason = "you already have a mentor";
            }
            else if (!CanTeachStudent(interactor, subject))
            {
                disabled = true;
                reason = "you're already skilled enough";
            }

            string subjectName = GetSubjectDisplayName(subject);
            string label = disabled
                ? $"Ask to teach {subjectName} ({reason})"
                : $"Ask to teach {subjectName}";

            var capturedSubject = subject;
            var capturedInteractor = interactor;
            options.Add(new InteractionOption
            {
                Name = label,
                IsDisabled = disabled,
                Action = () => OnMentorshipEntryClicked(capturedInteractor, capturedSubject, disabled)
            });
        }

        return options.Count > 0 ? options : null;
    }

    private static string GetSubjectDisplayName(ScriptableObject subject)
    {
        if (subject is SkillSO skill) return skill.SkillName;
        if (subject is CombatStyleSO style) return style.StyleName;
        // AbilitySO and any future ScriptableObject fall back to the asset name
        return subject.name;
    }

    private void OnMentorshipEntryClicked(Character interactor, ScriptableObject subject, bool disabled)
    {
        if (disabled) return; // defensive — menu already prevents clicks on disabled entries
        if (interactor == null || subject == null) return;

        if (IsServer)
        {
            var invitation = new InteractionMentorship(subject);
            if (invitation.CanExecute(interactor, _character))
                invitation.Execute(interactor, _character);
            return;
        }

        // Remote client → route via ServerRpc on the interactor's own CharacterMentorship
        if (interactor.CharacterMentorship == null || _character.NetworkObject == null) return;
        string key = $"{subject.GetType().Name}:{subject.name}";
        interactor.CharacterMentorship.RequestMentorshipServerRpc(_character.NetworkObject.NetworkObjectId, key);
    }

    [Rpc(SendTo.Server)]
    public void RequestMentorshipServerRpc(ulong mentorNetId, string subjectAssetKey)
    {
        // Defensive lookups + re-validation. The client cannot be trusted.
        if (NetworkManager.Singleton == null) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(mentorNetId, out var mentorObj))
            return;
        var mentor = mentorObj != null ? mentorObj.GetComponent<Character>() : null;
        if (mentor == null || mentor.CharacterMentorship == null) return;

        // Resolve the subject by looking it up inside the mentor's own teachable list.
        // This is lookup AND security in one step: a client cannot request a subject
        // this mentor is not actually offering.
        var parts = subjectAssetKey?.Split(':');
        if (parts == null || parts.Length != 2) return;
        var subject = mentor.CharacterMentorship.GetTeachableSubjects()
            .FirstOrDefault(s => s != null && s.GetType().Name == parts[0] && s.name == parts[1]);
        if (subject == null) return;

        var invitation = new InteractionMentorship(subject);
        if (!invitation.CanExecute(_character, mentor)) return;
        invitation.Execute(_character, mentor);
    }
}
