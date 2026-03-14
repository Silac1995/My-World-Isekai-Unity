# Spine-Unity Patterns

Practical code examples for common Spine tasks in the project.

## Dynamic Skin Combing
Use `Skin.AddSkin` to combine multiple visual parts (Hair, Shirt, Pants) into one runtime skin.

```csharp
public void UpdateVisuals(string[] partSkinNames) {
    var skeleton = skeletonAnimation.Skeleton;
    var resultSkin = new Skin("combined-skin");
    var data = skeleton.Data;

    foreach (var partName in partSkinNames) {
        Skin partSkin = data.FindSkin(partName);
        if (partSkin != null) resultSkin.AddSkin(partSkin);
    }

    skeleton.SetSkin(resultSkin);
    skeleton.SetSlotsToSetupPose();
}
```

## Procedural Aiming (Override Mode)
Using `SkeletonUtilityBone` to make a character look at a target.

```csharp
public class SpineLookAt : MonoBehaviour {
    [SerializeField] private SkeletonUtilityBone headBone;
    [SerializeField] private Transform target;

    void LateUpdate() {
        if (target == null) return;
        
        // headBone must be set to 'Override' mode in inspector
        Vector3 localTarget = headBone.transform.parent.InverseTransformPoint(target.position);
        float angle = Mathf.Atan2(localTarget.y, localTarget.x) * Mathf.Rad2Deg;
        
        headBone.transform.localRotation = Quaternion.Euler(0, 0, angle);
    }
}
```

## Track Overlays
Playing an emote without interrupting the base walk/idle animation.

```csharp
public void PlayEmote(string animationName) {
    // Play on Track 1 (Overlay)
    var trackEntry = skeletonAnimation.AnimationState.SetAnimation(1, animationName, false);
    
    // Auto-clear track when done
    trackEntry.Complete += (entry) => {
        skeletonAnimation.AnimationState.AddEmptyAnimation(1, 0.2f, 0); 
    };
}
```

## Render Separation (Sandwiching)
Splitting a skeleton at runtime to place an object "inside" the draw order.

```csharp
public void SetupSeparation(SkeletonAnimation anim, List<Slot> separatorSlots) {
    var separator = SkeletonRenderSeparator.AddToSkeletonRenderer(anim);
    anim.separatorSlots.Clear();
    anim.separatorSlots.AddRange(separatorSlots);
    separator.enabled = true;
    
    // Now you can move GameObjects into the generated SkeletonPartsRenderer children
}
```

## Custom Slot Materials
Changing the material of a specific slot (e.g., making a sword glow).

```csharp
public void SetSlotGlow(SkeletonAnimation anim, string slotName, Material glowMat) {
    Slot slot = anim.Skeleton.FindSlot(slotName);
    if (slot != null) {
        anim.CustomSlotMaterials[slot] = glowMat;
    }
}
```

## Waiting for Spine Animation (Coroutine)
```csharp
IEnumerator PerformActionRoutine() {
    var track = skeletonAnimation.AnimationState.SetAnimation(0, "Attack", false);
    
    // Spine-Unity provides a specific yield instruction
    yield return new Spine.Unity.WaitForSpineAnimationComplete(track);
    
    Debug.Log("Attack finished!");
}
```
