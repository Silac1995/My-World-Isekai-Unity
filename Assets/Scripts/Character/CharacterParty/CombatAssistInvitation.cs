/// <summary>
/// Sent to player party members when a fellow party member enters combat nearby.
/// Goes through the standard CharacterInvitation pipeline (UI prompt, accept/refuse).
/// On accept, the player joins the fight via JoinBattleAsAlly.
/// </summary>
public class CombatAssistInvitation : InteractionInvitation
{
    private Character _fightingMember;

    public CombatAssistInvitation(Character fightingMember)
    {
        _fightingMember = fightingMember;
    }

    public override bool CanExecute(Character source, Character target)
    {
        if (_fightingMember == null || !_fightingMember.IsAlive()) return false;
        if (!_fightingMember.CharacterCombat.IsInBattle) return false;
        if (target == null || !target.IsAlive()) return false;
        if (target.CharacterCombat != null && target.CharacterCombat.IsInBattle) return false;
        return true;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return $"{_fightingMember.CharacterName} is under attack! Join the fight?";
    }

    public override void OnAccepted(Character source, Character target)
    {
        if (_fightingMember == null || !_fightingMember.CharacterCombat.IsInBattle) return;
        target.CharacterCombat.JoinBattleAsAlly(_fightingMember);
    }

    public override void OnRefused(Character source, Character target)
    {
        // No penalty for declining combat
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        // Players decide via UI prompt, NPCs auto-accept (handled by BTCond_FriendInDanger)
        return null;
    }

    public override string GetAcceptMessage() => "I'm coming!";
    public override string GetRefuseMessage() => "I can't right now.";
}
