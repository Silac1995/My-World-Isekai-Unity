using UnityEngine;
using System.Linq;

public class InteractionMentorship : InteractionInvitation
{
    public ScriptableObject SubjectToTeach { get; private set; }

    public InteractionMentorship(ScriptableObject subject = null)
    {
        SubjectToTeach = subject;
    }

    public override bool CanExecute(Character source, Character target)
    {
        // Source est l'élève, target est le maître potentiel
        CharacterMentorship targetMentorship = target.CharacterMentorship;
        CharacterMentorship sourceMentorship = source.CharacterMentorship;
        if (targetMentorship == null || sourceMentorship == null) return false;

        // On ne peut avoir qu'un seul mentor actif à la fois
        if (sourceMentorship.CurrentMentor != null) return false;

        var teachable = targetMentorship.GetTeachableSubjects();
        if (teachable == null || teachable.Count == 0) return false;

        // Si le sujet est déjà défini (Interaction ciblée)
        if (SubjectToTeach != null)
        {
            if (!teachable.Contains(SubjectToTeach)) return false;
            if (!CanStudentStillLearn(source, target, SubjectToTeach)) return false;
        }
        else
        {
            // Interaction générale: Y a-t-il au moins UN sujet que l'élève peut encore apprendre de ce Maître ?
            bool canLearnSomething = teachable.Any(subject => CanStudentStillLearn(source, target, subject));
            if (!canLearnSomething) return false;
        }

        return true;
    }

    private bool CanStudentStillLearn(Character student, Character mentor, ScriptableObject subject)
    {
        // Récupérer le niveau du mentor
        SkillTier mentorTier = SkillTier.Novice;
        if (subject is SkillSO skill)
            mentorTier = SkillTierExtensions.GetTierForLevel(mentor.CharacterSkills.GetSkillLevel(skill));
        else if (subject is CombatStyleSO style)
        {
            var expertise = mentor.CharacterCombat.KnownStyles.FirstOrDefault(s => s.Style == style);
            if (expertise != null) mentorTier = expertise.CurrentTier;
        }

        // Récupérer le niveau de l'élève
        SkillTier studentTier = SkillTier.Novice;
        if (subject is SkillSO studentSkill)
        {
            if (student.CharacterSkills.HasSkill(studentSkill))
                studentTier = SkillTierExtensions.GetTierForLevel(student.CharacterSkills.GetSkillLevel(studentSkill));
            else
                return true; // Ne connaît pas du tout la compétence, donc peut apprendre
        }
        else if (subject is CombatStyleSO studentStyle)
        {
            var expertise = student.CharacterCombat.KnownStyles.FirstOrDefault(s => s.Style == studentStyle);
            if (expertise != null)
                studentTier = expertise.CurrentTier;
            else
                return true; // Ne connaît pas du tout le style
        }

        // L'élève peut apprendre uniquement s'il est strictement inférieur au Mentor Tier - 1
        return (int)studentTier < (int)mentorTier - 1;
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        CharacterMentorship targetMentorship = target.CharacterMentorship;
        if (targetMentorship == null) return false;

        // L'évaluation est faite PAR la Cible (le Maître) ENVERS la Source (l'Élève)
        float acceptanceChance = targetMentorship.CalculateAcceptanceChance(source);

        // Jet de dé
        float roll = UnityEngine.Random.Range(0f, 100f);
        bool accepted = roll <= acceptanceChance;

        if (accepted)
        {
            Debug.Log($"<color=cyan>[Mentorship]</color> {target.CharacterName} accepte de mentorer {source.CharacterName} (Chances : {acceptanceChance:F1}% - Roll : {roll:F1})");
            return true;
        }
        else
        {
            Debug.Log($"<color=orange>[Mentorship]</color> {target.CharacterName} refuse de mentorer {source.CharacterName} (Chances : {acceptanceChance:F1}% - Roll : {roll:F1})");
            return false;
        }
    }

    private void AutoSelectSubject(Character source, Character target)
    {
        var mentorship = target.CharacterMentorship;
        if (mentorship == null) return;
        var teachable = mentorship.GetTeachableSubjects();
        
        // Prendre le premier enseignement dispo que l'élève peut encore apprendre
        var validSubject = teachable.FirstOrDefault(s => CanStudentStillLearn(source, target, s));
        if (validSubject != null)
        {
            SubjectToTeach = validSubject;
        }
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        if (SubjectToTeach == null) AutoSelectSubject(source, target);

        string subjectName = "something";
        if (SubjectToTeach is SkillSO skill) subjectName = skill.SkillName;
        else if (SubjectToTeach is CombatStyleSO style) subjectName = style.StyleName;

        return $"Can you mentor me in {subjectName}, {target.CharacterName}?";
    }

    public override string GetAcceptMessage() => "Of course, I'd be glad to teach you.";
    public override string GetRefuseMessage() => "I'm sorry, I don't have time to take on students right now.";

    public override void OnAccepted(Character source, Character target)
    {
        if (SubjectToTeach == null) AutoSelectSubject(source, target);

        CharacterMentorship sourceMentorship = source.CharacterMentorship;
        CharacterMentorship targetMentorship = target.CharacterMentorship;

        if (sourceMentorship != null && targetMentorship != null && SubjectToTeach != null)
        {
            var enrolledClass = targetMentorship.EnrollStudentToClass(source, SubjectToTeach);
            sourceMentorship.SetMentor(target, SubjectToTeach, enrolledClass);
            Debug.Log($"<color=cyan>[Mentorship]</color> {source.CharacterName} is now learning '{SubjectToTeach.name}' under {target.CharacterName}.");
            
            // Le mentor essaie de donner le cours immédiatement s'il vient d'accepter et qu'il est en Wander
            targetMentorship.TryScheduleImmediateClass(enrolledClass);
        }
    }

    public override void OnRefused(Character source, Character target)
    {
        if (source.CharacterRelation != null)
        {
            // Petit malus de refus
            source.CharacterRelation.UpdateRelation(target, -1);
        }
    }
}
