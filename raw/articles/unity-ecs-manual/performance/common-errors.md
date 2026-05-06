---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/common-errors.html
fetched: 2026-05-05
section: performance
---

# Common Error Messages

This documentation section outlines causes and solutions for errors from the safety system in Unity Entities 6.4.0.

## Errors Related to the Dependency Property

The `Dependency` property records job handles to prevent data races. The safety system detects when jobs aren't assigned to this property:

> "The system <SYSTEM NAME> reads <COMPONENT NAME> via <JOB NAME> but that type was not assigned to the Dependency property."

**Common causes:**
- JobHandles aren't assigned to the `Dependency` property
- The safety system cannot determine which component types the system uses, often due to `EntityQueries` created without `GetEntityQuery`
- An exception terminates `OnUpdate` before `Dependency` assignment (the real exception is logged separately)

## Errors Related to Parallel Writing

Parallel jobs may produce errors indicating unsafe concurrent access:

> "InvalidOperationException: <JOB FIELD> is not declared [ReadOnly] in a IJobParallelFor job."

This occurs with `IJobChunk`, `IJobEntity`, and `ParallelFor`/`ParallelForTransform` jobs.

**Common causes:**
- Actually only reading data (add `[ReadOnly]` attribute)
- Using native containers without the `[ReadOnly]` attribute
- Using `ComponentLookup` in parallel jobs without `[ReadOnly]`

If concurrent writes are guaranteed safe, use `[NativeDisableParallelForRestriction]` to suppress the error.

## Errors Related to Missing Dependencies on Previously Scheduled Jobs

When multiple jobs access the same data concurrently:

> "InvalidOperationException: The previously scheduled job <JOB NAME> writes to the <OBJECT TYPE>..."

This commonly occurs when scheduling multiple job copies with different shared component values. Use `[NativeDisableContainerSafetyRestriction]` on affected fields to disable this error.

## Errors Related to Safety Handles

Accessing resources without proper access levels produces errors like:

> "InvalidOperationException: The <JOB FIELD> has been declared as [WriteOnly] in the job, but you are reading from it."

A more likely cause is reading from jobs launched within other jobs, which isn't supported and creates incorrect safety handles.

## Errors Related to System Type Definitions

All system types and `IJobEntity` implementations require the `partial` keyword for source generators:

> "error CS0101: The namespace <NAMESPACE> already contains a definition for <SYSTEM/JOB TYPE>"

Additionally, `SystemBase`-derived systems must be `class`, while `ISystem`-implementing systems must be `struct`.

---

## Outgoing Links

- [Dependency Property Documentation](../api/Unity.Entities.SystemState.Dependency.html#Unity_Entities_SystemState_Dependency)
- [EntityQuery API](../api/Unity.Entities.EntityQuery.html)
- [GetEntityQuery API](../api/Unity.Entities.SystemState.GetEntityQuery.html)
- [IJobChunk API](../api/Unity.Entities.IJobChunk.html)
- [IJobEntity API](../api/Unity.Entities.IJobEntity.html)
- [ReadOnly Attribute](https://docs.unity3d.com/ScriptReference/Unity.Collections.ReadOnly.html)
- [ComponentLookup API](../api/Unity.Entities.ComponentLookup-1.html)
- [NativeDisableParallelForRestriction](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeDisableParallelForRestrictionAttribute.html)
- [NativeDisableContainerSafetyRestriction](https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestrictionAttribute.html)
- [Concepts: Safety System](concepts-safety.html)
- [Terms of Use](https://docs.unity3d.com/Manual/TermsOfUse.html)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
