/// <summary>
/// Optional interface for visuals that support overlay animations on multiple tracks.
/// Maps to Spine's multi-track system (Track 0 = base, Track 1+ = overlays).
/// </summary>
public interface IAnimationLayering
{
    void PlayOverlayAnimation(AnimationKey key, int layer, bool loop = false);
    void PlayOverlayAnimation(string customKey, int layer, bool loop = false);
    void ClearOverlayAnimation(int layer);
}
