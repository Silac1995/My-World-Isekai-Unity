---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-system-inspector.html
fetched: 2026-05-05
section: editor
---

# System Inspector Reference

## Overview

When selecting a system in the Unity Editor, the Inspector presents information across two tabs:

- **Queries:** Shows the queries the system executes and associated components
- **Relationships:** Displays matched entities and scheduling constraints

![Systems Inspector - Queries (Left), Relationships (Right)](images/editor-system-inspectors.png)

## Queries Tab

This section lists all queries executed by the selected system alongside their component dependencies. The display indicates access permissions for each component as either **Read** or **Read & Write** permissions.

Clicking the navigation icon next to any component name (![Go to icon](images/editor-go-to.png)) redirects the selection to that component and opens the corresponding Component Inspector when available.

## Relationships Tab

This section contains two subsections:

### Entities
Lists all entities matched by the system, organized by their associated queries. When displaying many entities, a **Show All** option becomes available, which opens a dedicated Query window displaying the complete matching entity list.

### Scheduling Constraints
Enumerates systems affected by C# attributes constraining execution order. The selected system executes prior to systems in the **Before** group and following systems listed in the **After** group.

---

## Outgoing Links

- [Inspector Guide](https://docs.unity3d.com/Manual/UsingTheInspector.html)
- [Systems and Queries Documentation](systems-entityquery.html)
- [Component Inspector Reference](editor-component-inspector.html)
- [System User Manual](concepts-systems.html)
- [Systems Window Reference](editor-systems-window.html)
- [Entity Inspector Reference](editor-entity-inspector.html)
- [Query Window Reference](editor-query-window.html)
- [Unity Terms of Use](https://docs.unity3d.com/Manual/TermsOfUse.html)
- [Unity Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
- [Do Not Sell Personal Information](https://unity.com/legal/do-not-sell-my-personal-information)
