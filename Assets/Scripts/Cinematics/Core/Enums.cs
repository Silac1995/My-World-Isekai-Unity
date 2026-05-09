namespace MWI.Cinematics
{
    public enum TriggerAuthority { AnyPlayer, HostOnly }

    public enum PlayMode         { OncePerWorld, OncePerPlayer, OncePerNpc, Repeatable }

    public enum AdvanceMode      { AllMustPress, AnyAdvances, TriggerOnly }

    public enum InitiatorFilter  { AnyCharacter, PlayerOnly }

    public enum ParticipantsMode { Anyone, RequireAtLeastOne, RestrictedToSet }

    public enum CompletionMode   { AllComplete, AnyComplete, FirstComplete }

    public enum CinematicEndReason
    {
        Completed,
        Aborted,
        ActorLost,
        AllPlayersDisconnected
    }
}
