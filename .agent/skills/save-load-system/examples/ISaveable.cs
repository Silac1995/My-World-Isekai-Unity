public interface ISaveable
{
    string SaveKey { get; }       // Unique key in the root save container
    object CaptureState();        // Returns a serializable DTO
    void   RestoreState(object state);
}
