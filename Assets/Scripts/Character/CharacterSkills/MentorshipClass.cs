using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Représente une classe d'enseignement formelle reliant un Maître, un Sujet et ses Élèves inscrits.
/// </summary>
[System.Serializable]
public class MentorshipClass
{
    [SerializeField] private Character _mentor;
    [SerializeField] private ScriptableObject _teachingSubject; // SkillSO ou CombatStyleSO
    [SerializeField] private List<Character> _enrolledStudents = new List<Character>();

    public Character Mentor => _mentor;
    public ScriptableObject TeachingSubject => _teachingSubject;
    public IReadOnlyList<Character> EnrolledStudents => _enrolledStudents;

    public event System.Action<MentorshipClass> OnClassStarted;
    public event System.Action<MentorshipClass> OnClassEnded;

    public MentorshipClass(Character mentor, ScriptableObject subject)
    {
        _mentor = mentor;
        _teachingSubject = subject;
        _enrolledStudents = new List<Character>();
    }

    /// <summary>
    /// Ajoute un élève à la classe de ce mentor.
    /// Retourne true si l'inscription a réussi.
    /// </summary>
    public bool EnrollStudent(Character student)
    {
        if (student == null || student == _mentor) return false;
        if (!_enrolledStudents.Contains(student))
        {
            _enrolledStudents.Add(student);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Retire un élève de cette classe.
    /// </summary>
    public void RemoveStudent(Character student)
    {
        if (_enrolledStudents.Contains(student))
        {
            _enrolledStudents.Remove(student);
        }
    }

    /// <summary>
    /// Notifie tous les abonnés (élèves) que le cours a commencé.
    /// </summary>
    public void NotifyClassStarted()
    {
        OnClassStarted?.Invoke(this);
    }

    /// <summary>
    /// Notifie tous les abonnés (élèves) que le cours est terminé.
    /// </summary>
    public void NotifyClassEnded()
    {
        OnClassEnded?.Invoke(this);
    }

    /// <summary>
    /// Retourne vrai si la classe possède au moins un élève actif.
    /// </summary>
    public bool HasStudents()
    {
        // Nettoie la liste des potentiels élèves morts ou détruits avant de vérifier
        _enrolledStudents.RemoveAll(s => s == null || !s.IsAlive());
        return _enrolledStudents.Count > 0;
    }
}
