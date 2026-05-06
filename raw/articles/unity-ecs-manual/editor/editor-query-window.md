---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-query-window.html
fetched: 2026-05-05
section: editor
---

# Query Window Reference

## Overview

The Query window provides detailed information about a selected query. To access it, open the Relationship tab in an Entity, System, or Component Inspector and select the button next to a query. This opens a window displaying information about the query's related components and Entities.

![Query window button highlighted in the Relationships tab of an Entity Inspector.](images/editor-query-window-highlight.png)

## Window Tabs

The Query window contains two main tabs:

**Components Tab**
Lists components that the selected query searches for, including their access rights (**Read** or **Read & Write**).

**Entities Tab**
Displays a list of entities that match the query.

![Query window, Component view (left), Entities view (right)](images/editor-query-windows.png)

## Features

The tab header displays the query number, indicating the declaration order in the C# system definition. 

Selecting any component or entity in the view displays additional information in the Inspector. To view information about the associated system, click the navigation icon (![Go to icon](images/editor-go-to.png)) next to the system name. This updates the selection to that system and displays the System Inspector where available.

## Additional Resources

- [Entity Query user manual](systems-entityquery.html)
- [System Inspector reference](editor-system-inspector.html)

---

### Outgoing Hyperlinks

- http://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
