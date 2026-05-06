---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/transforms-comparison.html
fetched: 2026-05-05
section: systems
---

# Transforms Comparison | Entities 6.4.0

Many operations from the `UnityEngine.Transform` class are available in the Entities package with key syntax differences.

## Unity Engine Transform Property Equivalents

| UnityEngine Property | ECS Equivalent |
|---|---|
| `childCount` | Use `SystemAPI.GetBuffer<Child>(e).Length` |
| `forward` | Use `math.normalize()` with `LocalToWorld.Forward` (omit normalize if no scale in hierarchy) |
| `localPosition` | Use `LocalTransform.Position` |
| `localRotation` | Use `LocalTransform.Rotation` |
| `localScale` | Use `LocalTransform.Scale` and `PostTransformMatrix` |
| `localToWorldMatrix` | Use `LocalToWorld.Value` |
| `lossyScale` | Use `LocalToWorld.Value` with `math.length()` from Mathematics package |
| `parent` | Use `Parent.Value` |
| `position` | Use `LocalToWorld.Position` |
| `right` | Use `math.normalize()` with `LocalToWorld.Right` |
| `root` | Use `Parent.Value` in a loop until no parent exists |
| `rotation` | Use `LocalToWorld.Value.Rotation()` |
| `up` | Use `math.normalize()` with `LocalToWorld.Up` |
| `worldToLocalMatrix` | Use `math.inverse()` with `LocalToWorld.Value` |

### Code Example: Local Position
```csharp
float3 localPosition(ref SystemState state, Entity e)
{
  return SystemAPI.GetComponent<LocalTransform>(e).Position;
}
```

### Code Example: Local Scale
```csharp
float3 localScale(ref SystemState state, Entity e)
{
  float scale = SystemAPI.GetComponent<LocalTransform>(e).Scale;
  if (SystemAPI.HasComponent<PostTransformMatrix>(e))
  {
    float4x4 ptm = SystemAPI.GetComponent<PostTransformMatrix>(e).Value;
    float lx = math.length(ptm.c0.xyz);
    float ly = math.length(ptm.c1.xyz);
    float lz = math.length(ptm.c2.xyz);
    return new float3(lx, ly, lz) * scale;
  }
  else
  {
    return new float3(scale, scale, scale);
  }
}
```

### Properties with No Equivalent

The following properties have no ECS equivalent:

- `eulerAngles`
- `localEulerAngles`
- `hasChanged`
- `hierarchyCapacity` (no limit on children)
- `hierarchyCount`

## Unity Engine Transform Method Equivalents

| UnityEngine Method | ECS Equivalent |
|---|---|
| `DetachChildren` | Remove `Parent` component from children via buffer |
| `GetChild` | Access `Child` buffer by index |
| `GetLocalPositionAndRotation` | Get from `LocalTransform` component |
| `GetPositionAndRotation` | Get from `LocalToWorld.Value` |
| `InverseTransformDirection` | Use `math.inverse()` on `LocalToWorld.Value` |
| `InverseTransformPoint` | Use `math.inverse()` on `LocalToWorld.Value` |
| `InverseTransformVector` | Use `math.inverse()` on `LocalToWorld.Value` |
| `IsChildOf` | Check `Parent.Value` equality |
| `LookAt` | Calculate rotation using `quaternion.LookRotationSafe()` |
| `Rotate` | Multiply quaternions for rotation |
| `RotateAround` | Use `quaternion.AxisAngle()` with position adjustment |
| `SetLocalPositionAndRotation` | Use `LocalTransform.FromPositionRotation()` |
| `SetParent` | Set `Parent` component; with `worldPositionStays`, convert matrices |
| `SetPositionAndRotation` | Set via `LocalTransform.FromPositionRotation()` |
| `TransformDirection` | Use `LocalToWorld.Value.TransformDirection()` |
| `TransformPoint` | Use `LocalToWorld.Value.TransformPoint()` |
| `TransformVector` | Use `LocalToWorld.Value.TransformDirection()` |
| `Translate` | Add to `LocalTransform.Position` |

### Code Example: Set Parent with World Position Stays
```csharp
void SetParent(ref SystemState state, Entity e, Entity parent)
{
  float4x4 childL2W = SystemAPI.GetComponent<LocalToWorld>(e).Value;
  float4x4 parentL2W = SystemAPI.GetComponent<LocalToWorld>(parent).Value;
  float4x4 temp = math.mul(math.inverse(parentL2W), childL2W);

  SystemAPI.SetComponent(e, new Parent { Value = parent});
  SystemAPI.SetComponent(e, LocalTransform.FromMatrix(temp));
}
```

### Code Example: Rotate (Space.World with Parent)
```csharp
void Rotate(ref SystemState state, Entity e, quaternion rotation)
{
  if (SystemAPI.HasComponent<Parent>(e))
  {
    Entity parent = SystemAPI.GetComponent<Parent>(e).Value;
    float4x4 parentL2W = SystemAPI.GetComponent<LocalToWorld>(parent).Value;
    rotation = math.inverse(parentL2W).TransformRotation(rotation);
  }
  LocalTransform transform = SystemAPI.GetComponent<LocalTransform>(e);
  rotation = math.mul(rotation, transform.Rotation);
  SystemAPI.SetComponent(e, transform.WithRotation(rotation));
}
```

### Methods with No Equivalent

The following methods have no ECS equivalent:

- `Find` (name-based search)
- `GetSiblingIndex` (children in arbitrary order)
- `SetAsFirstSibling` (children in arbitrary order)
- `SetAsLastSibling` (children in arbitrary order)
- `SetSiblingIndex` (children in arbitrary order)

## Additional Resources

- [Using transforms](transforms-using.html)

---

## Outgoing Links

- https://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Transform.html - UnityEngine.Transform
- https://docs.unity3d.com/Packages/com.unity.mathematics@1.2/api/Unity.Mathematics.math.normalize.html - normalize
- https://docs.unity3d.com/Packages/com.unity.mathematics@1.2/api/Unity.Mathematics.math.length.html - length
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
