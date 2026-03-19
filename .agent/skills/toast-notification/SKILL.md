---
name: toast-notification
description: Instructions and architecture for the Toast Notification system, including object pooling and decoupled event channels.
---

# Toast Notification System

The Toast Notification System is used to show brief, non-intrusive messages to the user (e.g., "Picked up Item", "Level Up", "Experience Gained"). It uses a decoupled event-driven architecture with an Object Pool to prevent memory allocations during gameplay.

## When to use this skill
- When you need to inform the player about an action without interrupting their gameplay (e.g., gathering, crafting, picking up an item).
- When debugging issues where toasts aren't appearing or are crashing the UI.

## The Decoupled Object Pool Architecture

Instead of directly instantiating a Toast UI element, systems communicate via a `ToastNotificationChannel` ScriptableObject.

### 1. ToastNotificationChannel (ScriptableObject)
The communication bridge between gameplay code and the UI.
**Rule:** Gameplay scripts should use dependency injection (e.g., passed via `InitializeNotifications`) or inspector references to access the channel.
- Call `channel.Raise(new ToastNotificationPayload(...))` to trigger a toast.
- The payload contains the text, duration, icon, and visual type (Info, Warning, Error, Success).

### 2. UI_ToastManager (MonoBehaviour)
Listens to the `ToastNotificationChannel` and manages an Object Pool of `UI_ToastElement`.
- It pre-allocates an initial pool of toast objects on `Start()`.
- When a toast is requested, it dequeues an available toast. If the pool is empty, it instantiates a new one and expands the pool dynamically.
- **Rule:** Never `Destroy()` a toast element. The manager handles returning them to the pool.

### 3. UI_ToastElement (MonoBehaviour)
The individual UI component.
- Fades in, holds for `Duration`, and fades out via a Coroutine.
- Once finished, it invokes a callback (`_returnToPoolAction`) to safely return to the manager's queue instead of calling `Destroy(gameObject)`.

## Existing Components
- `MWI.UI.Notifications.ToastNotificationPayload` -> The data structure carrying the message.
- `MWI.UI.Notifications.ToastNotificationChannel` -> The SO channel handling events.
- `MWI.UI.Notifications.UI_ToastManager` -> The pool manager (usually living on the Player UI Canvas).
- `MWI.UI.Notifications.UI_ToastElement` -> The visual representation of a single toast.

[See `examples/raising_toast.md` for implementation details.]
