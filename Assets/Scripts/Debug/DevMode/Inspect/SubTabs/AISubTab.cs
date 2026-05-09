public class AISubTab : CharacterSubTab
{
    protected override string RenderContent(Character c)
    {
        return CharacterAIDebugFormatter.FormatAll(c);
    }
}
