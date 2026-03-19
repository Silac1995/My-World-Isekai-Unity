# Toast Notification Example

Here are common ways to interact with the Toast Notification System.

## Raising a Toast

You need a reference to the `ToastNotificationChannel`. If you don't have one, inject it from a parent wrapper or the main `CharacterEquipment` initialization.

```csharp
public class CharacterEquipment : CharacterSystem
{
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;

    public bool PickUpItem(ItemInstance item)
    {
        // 1. Pick up item logic...
        
        // 2. Raise the toast if channel is available
        if (_toastChannel != null)
        {
            _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: $"Picked up {item.ItemSO.ItemName}",
                type: MWI.UI.Notifications.ToastType.Info,
                duration: 3f,
                icon: item.ItemSO.Icon
            ));
        }

        return true;
    }
}
```

## Adding The UI Manager

Ensure your `UICanvas` has exactly one `UI_ToastManager` in the scene.
The manager needs rules set up in the inspector for:
1. `_toastChannel` (The scriptable object channel)
2. `_toastPrefab` (The UI_ToastElement prefab to spawn)
3. `_toastContainer` (The VerticalLayoutGroup where toasts expand downwards or upwards)

If the prefab is not assigned, the manager will safely ignore requests to avoid crashing other systems.
