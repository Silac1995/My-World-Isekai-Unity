---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/entities-journaling.html
fetched: 2026-05-05
section: performance
---

# Journaling

The Journaling feature enables you to document every action performed in your project for debugging purposes. It tracks activities such as world and entity creation/destruction, system additions/removals, and component modifications.

## Enable Journaling

You have two options to activate Journaling:

- Access the Journaling window via **Window > Entities > Journaling**
- Toggle it in the Preferences window at **Preferences > Entities > Journaling**

You can also use the `DISABLE_ENTITIES_JOURNALING` preprocessor directive to strip all Journaling code from your project during development.

## Assign Memory to Journaling

Configure memory allocation for Journaling by adjusting the Total Memory MB property in **Preferences > Entities > Journaling**.

The system operates on a first-in-first-out basis, meaning newer records overwrite older ones when capacity is reached. To retain historical data longer, increase the allocated memory size.

## Journaling Window

Open the window through **Window > Entities > Journaling**. Recording begins immediately and continues in both Edit and Play modes. When paused, you can examine specific records and review associated systems, entities, or components. A search function helps locate records quickly.

The system captures all `GetComponentDataRW` calls to identify write operations. However, it converts these to `SetComponentData` by cross-referencing records to locate the previous `GetRW` caller on the same chunk—likely identifying the system responsible for the change. Note that final calls may lack corresponding future accesses and cannot be converted.

## Inspect Records in Your Code

To examine Journaling records programmatically:

1. Set a breakpoint in your code
2. Use APIs from the `Unity.Entities.EntitiesJournaling` namespace to retrieve and examine records

Each record receives an unsigned 64-bit integer index and falls into these categories:

- World created or destroyed
- Entity created or destroyed
- System added or removed
- Component added or removed
- Component data set
- Component data get (read-write only)

When selecting an index, available information includes the executing system, origin system, frame index, record type, affected world, entity list, component type list, and associated data.

---

## Outgoing Links

- https://docs.unity3d.com/Manual/Preferences.html - Preferences
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
