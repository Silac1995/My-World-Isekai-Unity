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
        if (targetMentorship == null) return false;

        var teachable = targetMentorship.GetTeachableSubjects();
        if (teachable == null || teachable.Count == 0) return false;

        if (SubjectToTeach != null && !teachable.Contains(SubjectToTeach))
            return false;

        // Pour ce TEST, on retire l'exigence de relation (par défaut RelationValue = 0 au spawn)
        // if (source.CharacterRelation != null)
        // {
        //     var rel = source.CharacterRelation.GetRelationshipWith(target);
        //     if (rel == null) return false;
        //     return rel.RelationValue >= 10;
        // }

        return true;
    }

    private void AutoSelectSubject(Character source, Character target)
    {
        var mentorship = target.CharacterMentorship;
        if (mentorship == null) return;
        var teachable = mentorship.GetTeachableSubjects();
        if (teachable.Count > 0)
        {
            // Prendre le premier enseignement dispo (Idéalement, on filtrerait pour ceux que source n'a pas)
            SubjectToTeach = teachable[0];
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
            sourceMentorship.SetMentor(target, SubjectToTeach);
            targetMentorship.EnrollStudentToClass(source, SubjectToTeach);
            Debug.Log($"<color=cyan>[Mentorship]</color> {source.CharacterName} is now learning '{SubjectToTeach.name}' under {target.CharacterName}.");
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
