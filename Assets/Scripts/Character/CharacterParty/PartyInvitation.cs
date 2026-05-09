using UnityEngine;

public class PartyInvitation : InteractionInvitation
{
    private SkillSO _leadershipSkill;

    public PartyInvitation(SkillSO leadershipSkill)
    {
        _leadershipSkill = leadershipSkill;
    }

    public override bool CanExecute(Character source, Character target)
    {
        if (source == null || target == null) return false;
        if (source == target) return false;
        if (!target.IsAlive()) return false;

        // Source must have Leadership skill
        if (_leadershipSkill != null && !source.CharacterSkills.HasSkill(_leadershipSkill)) return false;

        // Target must not be in any party
        if (target.CharacterParty != null && target.CharacterParty.IsInParty) return false;

        // Check party capacity
        CharacterParty sourceParty = source.CharacterParty;
        if (sourceParty != null && sourceParty.IsInParty)
        {
            int maxSize = Mathf.Min(2 + source.CharacterSkills.GetSkillLevel(_leadershipSkill), 8);
            if (sourceParty.PartyData.IsFull(maxSize)) return false;
        }

        return true;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return "Want to join my group?";
    }

    public override void OnAccepted(Character source, Character target)
    {
        CharacterParty sourceParty = source.CharacterParty;

        // Auto-create party if source doesn't have one
        if (sourceParty != null && !sourceParty.IsInParty)
        {
            sourceParty.CreateParty();
        }

        if (sourceParty != null && sourceParty.IsInParty)
        {
            target.CharacterParty.JoinParty(sourceParty.PartyData.PartyId);
        }
    }

    public override void OnRefused(Character source, Character target)
    {
        // No relation impact — spec requirement
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        return null;
    }
}
