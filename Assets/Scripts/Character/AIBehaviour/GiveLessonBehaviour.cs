using UnityEngine;
using System.Linq;

public class GiveLessonBehaviour : IAIBehaviour
{
    public bool IsFinished { get; private set; }
    
    private float _tickTimer = 0f;
    private float _tickRate = 2f; // XP given every 2 seconds
    private float _baseXPPerTick = 10f;

    private MentorClassZone _myClassZone;

    public void Act(Character character)
    {
        if (IsFinished) return;

        if (_myClassZone == null)
        {
            var mentorship = character.CharacterMentorship;
            if (mentorship != null)
            {
                _myClassZone = mentorship.SpawnedClassZone;
            }

            // Attendre que la classe soit instanciée
            if (_myClassZone == null) return; 
        }

        _tickTimer += Time.deltaTime;
        if (_tickTimer >= _tickRate)
        {
            _tickTimer = 0f;
            GiveXPTick(character);
        }
    }

    private void GiveXPTick(Character mentor)
    {
        if (_myClassZone == null || _myClassZone.ActiveStudents.Count == 0) return;

        var mentorship = mentor.CharacterMentorship;
        if (mentorship == null) return;

        SkillTier mentorTier = SkillTier.Novice;

        if (_myClassZone.TeachingSkill is SkillSO skillSO)
        {
            var skills = mentor.CharacterSkills;
            if (skills != null && skills.HasSkill(skillSO))
                mentorTier = SkillTierExtensions.GetTierForLevel(skills.GetSkillLevel(skillSO));
            else return; 
        }
        else if (_myClassZone.TeachingSkill is CombatStyleSO combatSO)
        {
            var combat = mentor.CharacterCombat;
            if (combat != null)
            {
                var expertise = combat.KnownStyles.FirstOrDefault(s => s.Style == combatSO);
                if (expertise != null) mentorTier = expertise.CurrentTier;
                else return;
            }
            else return;
        }

        // Distribuer l'XP
        var students = _myClassZone.ActiveStudents.ToList();
        foreach (var student in students)
        {
            if (student == null) continue;
            
            var studentMentorship = student.CharacterMentorship;
            if (studentMentorship != null && studentMentorship.CurrentMentor == mentor)
            {
                studentMentorship.ReceiveLessonTick(_myClassZone.TeachingSkill, mentorTier, _baseXPPerTick);
            }
        }
    }

    public void Exit(Character character)
    {
        IsFinished = true;
    }

    public void Terminate()
    {
        IsFinished = true;
    }
}
