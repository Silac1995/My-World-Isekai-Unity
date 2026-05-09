[System.Flags]
public enum MovementMode : byte
{
    Walk   = 1 << 0,
    Run    = 1 << 1,
    Fly    = 1 << 2,
    Swim   = 1 << 3,
    Burrow = 1 << 4
}
