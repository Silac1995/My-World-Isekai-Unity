namespace MWI.Ambition
{
    /// <summary>
    /// Argument to TaskBase.OnControllerSwitching. Tells a task whether the Character
    /// is becoming Player-driven or NPC-driven so it can clean up the appropriate
    /// in-flight driver (queued CharacterAction or transient GOAP goal).
    /// </summary>
    public enum ControllerKind
    {
        Player,
        NPC
    }
}
