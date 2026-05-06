---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-entity-inspector.html
fetched: 2026-05-05
section: editor
---

# Entity Inspector Reference

## Overview

The Inspector window displays entity information differently depending on the selected data mode. When you choose an entity from the Entity Hierarchy window, the Inspector presents its details accordingly.

## Authoring Data Mode

Indicated by a white or gray circle icon, this mode displays information about the selected GameObject. Users can modify and adjust GameObject properties through the standard Inspector interface. Selecting an entity in the Entity Hierarchy window shows details about that entity's corresponding authoring GameObject.

## Runtime Data Mode

Represented by an orange or red circle icon, this mode presents entity data across three tabs:

- **Components:** Lists all components attached to an entity, functioning similarly to MonoBehaviours on a GameObject
- **Relationships:** Shows systems that interact with the selected entity, populated only when the entity has components matching a system query

### Components and Aspects Tab

Field states vary by editor mode:

- **Edit mode:** Fields display as read-only
- **Play mode:** Fields become editable for debugging; orange or red vertical bars indicate data destruction upon exiting Play mode

### Relationships Tab

This section displays system queries matching the selected entity, including access rights (Read or Read & Write) for each component.

Two icons enable navigation:
- Arrow icon: Switches selection to the referenced system or component, opening the respective Inspector
- Window icon: Opens the Query window, displaying all entities matching the selected query

## Mixed Data Mode

Available only during Play mode, indicated by combined white/orange or gray/red circle icons. Properties with orange vertical bars have entity values overwriting authoring values. Editing these fields modifies entity data (lost on exit). Fields without bars edit authoring values (retained on exit).

## Related Resources

- [Entities user manual](concepts-entities.html)
- [Entities Hierarchy window reference](editor-hierarchy-window.html)
- [System Inspector reference](editor-system-inspector.html)
- [Component Inspector reference](editor-component-inspector.html)
- [Query window reference](editor-query-window.html)

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/Manual/UsingTheInspector.html — Inspector documentation
- https://docs.unity3d.com/Manual/TermsOfUse.html — Trademarks and terms of use
- https://unity.com/legal — Legal
- https://unity.com/legal/privacy-policy — Privacy Policy
- https://unity.com/legal/cookie-policy — Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information — Do Not Sell or Share My Personal Information
