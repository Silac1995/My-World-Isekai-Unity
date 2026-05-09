/// <summary>
/// Universal animation keys shared across all archetypes.
/// For archetype-specific animations, use the string-based PlayAnimation overload.
/// </summary>
public enum AnimationKey : byte
{
    Idle,
    Walk,
    Run,
    Attack,
    GetHit,
    Die,
    PickUp,
    Action
}
