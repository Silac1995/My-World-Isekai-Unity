---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-hierarchy-window.html
fetched: 2026-05-05
section: editor
---

# Entities Hierarchy Window Reference

## Overview

The Entities Hierarchy window serves as an Editor tool for visualizing the entity hierarchy across different worlds in your project. According to the documentation, it enables developers to "search for and visualize in their hierarchy" a large number of entities.

**Access:** Navigate to **Window > Entities > Hierarchy**

## Core Features

The window provides comparable functionality to the standard Hierarchy window for GameObjects, with the notable limitation that "you can't select multiple items at once."

### Search and Selection
- Filter entities by name, ID, or component
- World selection via dropdown menu (top left)
- View entity details in the Inspector window after selection

## Data Modes

### Authoring Data Mode
Indicated by a white or gray circle in the top right corner.

**Edit Mode Display:**
- GameObjects and Prefab instances (inside Sub Scenes)
- GameObjects and Prefab instances outside Sub Scenes

**Play Mode Display:**
- GameObjects and Prefab instances (if inside open Sub Scenes)
- Read-only entities (if inside closed Sub Scenes)
- Nothing displays for items outside Sub Scenes

### Runtime Data Mode
Indicated by an orange or red circle in the top right corner.

This mode reveals the runtime state representation of GameObjects and entities created outside the baking process, such as WorldTime.

**Visual Indicators:**
- Orange/red vertical bars = runtime data
- Entity prefab icon indicates prefab status
- Blue text with entity icon = entity prefab instance

### Mixed Data Mode
Indicated by concentric white/orange or gray/red circles. Available only during Play mode.

Combines both authoring and runtime views, displaying GameObjects in their authoring form alongside dynamically created entities.

## Configuration

Enable real-time conversion by accessing **Preferences > Entities > Baking > Live Baking** to view the complete entity hierarchy during both Edit and Play modes.

---

## Outgoing Hyperlinks

- https://docs.unity3d.com/ - docs.unity3d.com
- https://docs.unity3d.com/Manual/Hierarchy.html - Hierarchy window
- https://docs.unity3d.com/Manual/TermsOfUse.html - Trademarks and terms of use
- https://unity.com/legal - Legal
- https://unity.com/legal/privacy-policy - Privacy Policy
- https://unity.com/legal/cookie-policy - Cookie Policy
- https://unity.com/legal/do-not-sell-my-personal-information - Do Not Sell or Share My Personal Information
