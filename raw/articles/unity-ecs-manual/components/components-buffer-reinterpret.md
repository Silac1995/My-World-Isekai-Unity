---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/components-buffer-reinterpret.html
fetched: 2026-05-05
section: components
---

# Reinterpret a Dynamic Buffer

## Overview

You have the ability to reinterpret a `DynamicBuffer<T>` as another `DynamicBuffer<U>`, provided that both types have identical memory sizes. This technique proves useful when you need to view a dynamic buffer of components as a dynamic buffer of their associated entities. The reinterpretation creates an alias to the same underlying memory, meaning modifications at index `i` in one buffer affect the corresponding index in the other.

## Important Considerations

**Size Requirements:** "The `Reinterpret` method only enforces that the original type and new type have the same size."

For instance, converting a `uint` to a `float` is valid since both occupy 32 bits. However, you must verify that the reinterpretation aligns with your actual use case.

**Safety Handling:** Reinterpreted buffers inherit the safety handle from the source buffer, making them subject to identical safety constraints.

## Code Example

The following demonstrates reinterpreting a dynamic buffer. This assumes a `MyElement` dynamic buffer exists containing a single `int` field named `Value`:

```csharp
public class ExampleSystem : SystemBase
{
    private void ReinterpretEntitysChunk(Entity e)
    {
        DynamicBuffer<MyElement> myBuff = EntityManager.GetBuffer<MyElement>(e);

        // Valid as long as each MyElement struct is four bytes. 
        DynamicBuffer<int> intBuffer = myBuff.Reinterpret<int>();

        intBuffer[2] = 6;  // same effect as: myBuff[2] = new MyElement { Value = 6 };

        // The MyElement value has the same four bytes as int value 6. 
        MyElement myElement = myBuff[2];
        Debug.Log(myElement.Value);    // 6
    }
}
```

---

## Outgoing Links

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
