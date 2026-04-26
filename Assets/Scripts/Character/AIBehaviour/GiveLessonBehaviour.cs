using UnityEngine;
using System.Linq;

public class GiveLessonBehaviour : IAIBehaviour
{
    public bool IsFinished { get; private set; }
    
    private float _tickTimer = 0f;
    private float _tickRate = 2f; // XP given every 2 seconds
    private float _baseXPPerTick = 10f;
    
    private int _relationshipTickCounter = 0;

    private MentorClassZone _myClassZone;

    public void Enter(Character selfCharacter) { }
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

            // Wait for the class to be instantiated
            if (_myClassZone == null) return;
        }

        // Face the class by positioning the mentor appropriately
        Vector3 directionToZone = _myClassZone.transform.position - character.transform.position;
        directionToZone.y = 0;
        if (directionToZone.sqrMagnitude > 0.1f && character.CharacterMovement != null)
        {
            Transform movementTransform = character.CharacterMovement.transform;
            movementTransform.rotation = Quaternion.RotateTowards(movementTransform.rotation, Quaternion.LookRotation(directionToZone), Time.deltaTime * 360f);
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

        _relationshipTickCounter++;
        bool shouldIncreaseRelation = _relationshipTickCounter >= 4;
        if (shouldIncreaseRelation)
        {
            _relationshipTickCounter = 0;
        }

        // Distribute XP and relations
        var students = _myClassZone.ActiveStudents.ToList();
        foreach (var student in students)
        {
            if (student == null) continue;
            
            var studentMentorship = student.CharacterMentorship;
            if (studentMentorship != null && studentMentorship.CurrentMentor == mentor)
            {
                studentMentorship.ReceiveLessonTick(_myClassZone.TeachingSkill, mentorTier, _baseXPPerTick);
                
                if (shouldIncreaseRelation)
                {
                    // Mentor to Student
                    Relationship mentorToStudent = mentor.CharacterRelation?.AddRelationship(student);
                    if (mentorToStudent != null) mentorToStudent.IncreaseRelationValue(1);

                    // Student to Mentor
                    Relationship studentToMentor = student.CharacterRelation?.AddRelationship(mentor);
                    if (studentToMentor != null) studentToMentor.IncreaseRelationValue(1);
                }
            }
        }
        
        // --- RANDOM SPEECH ---
        // 10% chance per tick (so roughly every 20 seconds on average)
        if (mentor.CharacterSpeech != null && Random.value < 0.1f)
        {
            string[] phrases = new string[]
            {
                "So today I'm going to teach you...",
                "Pay attention to the form.",
                "It's all in the wrist.",
                "Watch closely how I do this.",
                "Practice makes perfect.",
                "Don't rush it, take your time.",
                "Focus on the technique, not just power.",
                "A true master never stops learning.",
                "Are you all following me?",
                "Let's go over that one more time."
            };
            mentor.CharacterSpeech.Say(phrases[Random.Range(0, phrases.Length)]);
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
