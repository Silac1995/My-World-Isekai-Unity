---
source_url: https://docs.unity3d.com/Packages/com.unity.entities@6.4/manual/editor-authoring-runtime.html
fetched: 2026-05-05
section: editor
---

# Authoring and Runtime Data Modes

The Entities Hierarchy window and Entity Inspector support different data modes representing the types of data you can control:

## Data Mode Types

**Authoring Mode**
Contains version-controlled data such as assets and scene GameObjects. Displayed with a white or gray circle icon in the Editor.

**Runtime Mode**
Holds data that the runtime uses and modifies—information Unity destroys upon exiting Play mode. Shown as an orange or red circle icon.

**Mixed Mode**
Provides a view showing both runtime and authoring data, with authoring data taking priority. Represented by a white and orange (or gray and red) circle icon.

## Benefits of Mode Switching

Switching between data modes while in Play or Edit mode enables you to make permanent changes without entering or exiting Play mode. For example, you can modify level geometry during Play mode and save those changes while remaining active.

## Scene View Configuration

You can configure the Scene view to display only authoring data through **Preferences > Entities > Baking > Scene View Mode**. This prevents runtime-generated elements from cluttering the display in Runtime mode.

You can also switch to Runtime mode while in Edit mode to observe how Unity bakes and optimizes GameObjects without entering Play mode.

## Visual Highlighting

The Hierarchy and Inspector windows highlight runtime data destroyed upon exiting Play mode using:
- Orange (Editor Dark theme)
- Red (Editor Light theme)

This makes it easy to identify non-persistent data.

## Default Behavior

Select the data mode circle in the window's top right to choose from: Automatic, Authoring, Mixed, or Runtime.

**Automatic Mode** intelligently selects the appropriate mode based on your selection and current edit state.

| Operation | Default Data Mode |
|-----------|-------------------|
| Select an entity | Entity Inspector set to Runtime mode |
| Select a GameObject | Edit mode: Authoring mode; In Sub Scene during Play: Authoring mode; Outside Sub Scene during Play: Runtime mode |
| Enter Play mode | Mixed data mode for both windows |

Locking a specific mode adds an underline beneath the data mode circle.

## Authoring Subscenes in Play Mode

While in Play mode, you can author subscenes. Upon exiting Play mode, Unity retains any modifications made to subscene GameObjects that convert to entities at runtime.

---

## Outgoing Links

- [Entities Hierarchy window reference](https://docs.unity3d.com/Manual/editor-hierarchy-window.html)
- [Entity Inspector reference](https://docs.unity3d.com/Manual/editor-entity-inspector.html)
- [Entities Preferences reference](https://docs.unity3d.com/Manual/editor-preferences.html)
- [Trademarks and terms of use](https://docs.unity3d.com/Manual/TermsOfUse.html)
- [Legal](https://unity.com/legal)
- [Privacy Policy](https://unity.com/legal/privacy-policy)
- [Cookie Policy](https://unity.com/legal/cookie-policy)
- [Do Not Sell or Share My Personal Information](https://unity.com/legal/do-not-sell-my-personal-information)
