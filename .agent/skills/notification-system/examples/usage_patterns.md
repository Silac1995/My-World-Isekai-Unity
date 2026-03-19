# Notification System Usage Patterns

## 1. Triggering a Notification from Code
Any script that handles backend logic (like giving the player an item or unlocking a skill) should trigger the channel.

```csharp
using UnityEngine;
using MWI.UI.Notifications;

public class InventoryManager : MonoBehaviour
{
    // Inject the scriptable object channel via the Inspector
    [SerializeField] private NotificationChannel _inventoryNotificationChannel;

    public void AddItemToInventory(ItemData item)
    {
        // 1. Backend logic
        _items.Add(item);
        
        // 2. Notify the UI
        if (_inventoryNotificationChannel != null)
        {
            _inventoryNotificationChannel.Raise();
        }
    }
}
```

## 2. Setting up the UI Badge
On the UI side, you don't write new code. You use the generic `UI_NotificationBadge` component.

1. **Hierarchy Setup**:
   - `Inventory Button` (The button the user clicks)
     - `Badge Image` (A red dot visual, child of the button) -> Add `UI_NotificationBadge` here or on the button.

2. **Inspector Configuration for UI_NotificationBadge**:
   - `_channel`: Drag and drop the `SO_InventoryNotificationChannel` Asset.
   - `_badgeObject`: Drag the `Badge Image` GameObject here.
   - `_clearOnEnable`: Set to `true` if this badge should clear when the button is shown.
   - `_parentWindow`: (Optional) Drag the `Inventory Window Pnl` here. If this window is already active, `ShowBadge()` will abort and instantly clear the notification instead.

## 3. Manually Clearing a Notification
If you don't use the Auto-Hide feature (`_parentWindow` or `_clearOnEnable`), you must clear the channel manually when the player consumes/views the content.

```csharp
using UnityEngine;
using MWI.UI.Notifications;

public class UI_InventorySlot : MonoBehaviour
{
    [SerializeField] private NotificationChannel _inventoryNotificationChannel;

    public void OnSlotClicked()
    {
        // Player acknowledged the specific new item.
        if (_inventoryNotificationChannel != null)
        {
            _inventoryNotificationChannel.Clear();
        }
    }
}
```
