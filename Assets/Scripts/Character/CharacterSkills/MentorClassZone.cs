using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using System.Linq;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(NavMeshModifierVolume))]
public class MentorClassZone : MonoBehaviour
{
    [Header("Class Definition")]
    public Character Mentor;
    public ScriptableObject TeachingSkill; // Supporte SkillSO ou CombatStyleSO
    public List<Character> ActiveStudents = new List<Character>();

    public bool IsClassActive { get; private set; }

    [SerializeField] private BoxCollider _zoneCollider;
    [SerializeField] private NavMeshModifierVolume _navMeshModifier;

    public MentorshipClass LinkedClass { get; private set; }
    public ScheduleEntry ActiveSchedule { get; private set; }

    public void StartClass()
    {
        if (IsClassActive) return;
        IsClassActive = true;
        LinkedClass?.NotifyClassStarted();
        Debug.Log($"<color=yellow>[Class]</color> La classe de {TeachingSkill?.name} animée par {Mentor?.CharacterName} vient de commencer !");
    }

    public void EndClass()
    {
        if (!IsClassActive) return;
        IsClassActive = false;
        LinkedClass?.NotifyClassEnded();
        
        // Optionnel: Vider la liste d'élèves quand la classe ferme
        // ActiveStudents.Clear();
        
        Debug.Log($"<color=yellow>[Class]</color> La classe de {TeachingSkill?.name} animée par {Mentor?.CharacterName} est terminée.");
    }

    // Removed LinkedClass manual definition as it was moved up

    /// <summary>
    /// Initialise la zone d'enseignement simplifiée pour les préfabs déjà paramétrés et l'adapte au nombre d'élèves.
    /// </summary>
    public void InitializeClass(MentorshipClass mentorshipClass, ScheduleEntry schedule = null)
    {
        LinkedClass = mentorshipClass;
        ActiveSchedule = schedule;
        Mentor = mentorshipClass.Mentor;
        TeachingSkill = mentorshipClass.TeachingSubject;
        ActiveStudents.Clear();

        // --- Ajustement dynamique de la taille de la zone ---
        // Chaque personnage fait environ 5 unités de large/long (5x5).
        // On calcule le nombre d'élèves + le mentor (1) pour estimer la place requise
        int totalPeople = mentorshipClass.EnrolledStudents.Count + 1;
        float unitsPerPerson = 5f;

        // Une formule simple : on prend la racine carrée du nombre de personnes pour obtenir 
        // une grille carrée (ex: 4 personnes = grille de 2x2. 9 personnes = 3x3).
        int gridSide = Mathf.CeilToInt(Mathf.Sqrt(totalPeople));

        // +2 d'espacement pour que ce ne soit pas trop serré
        float newSize = (gridSide * unitsPerPerson) + 2f; 

        // Hauteur de 5 pour couvrir les personnages en Y, tout en étendant généreusement la zone en X et Z.
        Vector3 dynamicSize = new Vector3(newSize, 5f, newSize);

        if (_zoneCollider != null) 
            _zoneCollider.size = dynamicSize;

        if (_navMeshModifier != null) 
        {
            _navMeshModifier.size = dynamicSize;
            _navMeshModifier.center = _zoneCollider != null ? _zoneCollider.center : Vector3.zero;
        }
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
            // Vérifie que le personnage fait bien partie de la classe avant de le compter comme actif
            if (LinkedClass != null && LinkedClass.EnrolledStudents.Contains(chara))
            {
                if (!ActiveStudents.Contains(chara))
                {
                    ActiveStudents.Add(chara);
                }
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
