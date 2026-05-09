using System.Text;

public class InventorySubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("<b><color=#FFFFFF>Equipment</color></b>");
        var equip = c.CharacterEquipment;
        if (equip != null)
        {
            sb.AppendLine($"  {equip}");
        }
        else
        {
            sb.AppendLine("<color=grey>No CharacterEquipment</color>");
        }

        return sb.ToString();
    }
}
