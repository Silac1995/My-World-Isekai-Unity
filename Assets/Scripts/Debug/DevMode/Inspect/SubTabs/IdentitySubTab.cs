using System.Text;

public class IdentitySubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("<b><color=#FFFFFF>Identity</color></b>");
        sb.AppendLine($"Name: {c.CharacterName}");

        var bio = c.CharacterBio;
        if (bio != null)
        {
            sb.AppendLine($"Gender: {bio.Gender}");
            sb.AppendLine($"Age: {bio.Age}");
        }

        sb.AppendLine($"Race: {(c.Race != null ? c.Race.RaceName : "—")}");
        sb.AppendLine($"Archetype: {(c.Archetype != null ? c.Archetype.name : "—")}");
        sb.AppendLine($"Character Id: {c.CharacterId}");
        sb.AppendLine($"Origin World: {(string.IsNullOrEmpty(c.OriginWorldGuid) ? "—" : c.OriginWorldGuid)}");

        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>State</color></b>");
        sb.AppendLine($"Busy Reason: {c.BusyReason}");
        sb.AppendLine($"Is Alive: {c.IsAlive()}");
        sb.AppendLine($"Is Unconscious: {c.IsUnconscious}");
        sb.AppendLine($"Is Building: {c.IsBuilding}");
        sb.AppendLine($"Is Player: {c.IsPlayer()}");
        sb.AppendLine($"In Party: {c.IsInParty()}  |  Party Leader: {c.IsPartyLeader()}");

        if (c.IsAbandoned)
        {
            sb.AppendLine($"<color=#FF4444>Abandoned  |  Former Leader: {c.FormerPartyLeaderId}</color>");
        }

        return sb.ToString();
    }
}
