---
name: tooltip-system
description: Rules and implementation standards for using the global UI_TooltipManager to display hover descriptions across the game UI.
---

# Global UI Tooltip System

This skill dictates how to display dynamic, hovering tooltips across all UI elements in the project.

## Core Architecture
The system relies on a single **Singleton** instance: `UI_TooltipManager.Instance`. 
Because Netcode for GameObjects (NGO) isolates remote clients into separate processes, this standard static Singleton is fully multiplayer-safe for local client UI feedback and does **not** rely on `NetworkBehaviour`.

### The Prefab
The master Tooltip Prefab is located at: `Assets/Resources/UI/UI_TooltipManager.prefab`.
It must exist on the local player's Canvas (usually instantiated globally or placed in the main HUD Canvas hierarchy). It manages its own boundary checking so it does not render off-screen when the cursor approaches the screen edge.

## How to Implement Tooltips on a UI Element

Whenever you need a UI element (like an Item Slot, Skill Icon, or Status Effect) to display a tooltip on hover, follow these steps:

### 1. Implement Event Interfaces
The UI script must inherit from `UnityEngine.EventSystems.IPointerEnterHandler` and `IPointerExitHandler`.

### 2. Required Usings
```csharp
using UnityEngine.EventSystems;
```

### 3. Implementation Code

```csharp
public class UI_MyIcon : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private string _myDescription = "This is a detailed hover text.";

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Always check if the Instance exists in case the designer forgot to add the Prefab to the Canvas
        if (UI_TooltipManager.Instance != null && !string.IsNullOrEmpty(_myDescription))
        {
            // Call ShowTooltip, passing the desired text string
            UI_TooltipManager.Instance.ShowTooltip(_myDescription);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (UI_TooltipManager.Instance != null)
        {
            // Hide the tooltip when the mouse leaves the bounds of this UI element
            UI_TooltipManager.Instance.HideTooltip();
        }
    }
}
```

## Critical Rules
1. **Never duplicate the Tooltip GameObject:** Never instantiate the Tooltip inside local prefabs (like placing a Tooltip child under every single ItemSlot). Always use the `UI_TooltipManager` singleton so only one GameObject exists on the Canvas at any given point.
2. **Never leave empty strings:** Pass a validated, non-null, and non-empty string to `ShowTooltip()`.
3. **Raycast Targets:** Be extremely careful not to turn on `RaycastTarget` inside the Tooltip's Image or Text components. If they intercept raycasts, dragging the mouse might trigger a premature `OnPointerExit` on the underlying UI element, creating a flicker loop. The root prefab is already pre-configured to `raycastTarget = false`.
