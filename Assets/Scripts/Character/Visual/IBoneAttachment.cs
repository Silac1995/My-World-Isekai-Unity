using UnityEngine;

/// <summary>
/// Optional interface for visuals that support attaching GameObjects to skeleton bones.
/// Maps to Spine's BoneFollower / PointFollower system.
/// </summary>
public interface IBoneAttachment
{
    Transform GetBoneTransform(string boneName);
    void AttachToBone(string boneName, GameObject obj);
    void DetachFromBone(string boneName, GameObject obj);
}
