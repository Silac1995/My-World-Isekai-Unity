---
name: notification-system
description: Decoupled event-driven UI notification badges using NotificationChannel ScriptableObjects.
---

# Notification System

The Notification System is a decoupled, event-driven architecture designed to show UI badges (like "New Item" or "Level Up" dots) without tightly coupling gameplay logic to UI components.

## When to use this skill
- When implementing a new feature that requires notifying the player (e.g., gaining an item, unlocking a skill, receiving a quest).
- When building new UI menus that need reactive notification badges on their open/close buttons.
- When fixing bugs related to UI badges not clearing or appearing at the wrong time.

## The Decoupled Event Architecture
Instead of gameplay scripts directly calling `UI_Badge.Show()`, all communication happens through a `NotificationChannel` (a ScriptableObject).
Gameplay code "Raises" a notification on the channel. UI code listens to the channel and "Shows" or "Hides" itself.

### 1. NotificationChannel (ScriptableObject)
The central communication hub.
**Rule:** Create one `NotificationChannel` per distinct notification domain (e.g., `SO_InventoryNotificationChannel`, `SO_SkillsNotificationChannel`).
- Gameplay scripts only need a reference to this channel to trigger notifications.
- The UI completely ignores the source of the notification.

### 2. UI_NotificationBadge (MonoBehaviour)
The reactive UI component attached to the visual badge (e.g., a red circle Image on a Canvas Button).
**Rule:** Always ensure the badge unsubscribes from the channel on `OnDisable` to prevent memory leaks. The current `UI_NotificationBadge` already handles this correctly.
- Supports Auto-Clearing: Can automatically clear the channel when its parent window opens, ensuring the player doesn't see a badge while already looking at the new content.

### 3. Raising and Clearing
- `NotificationChannel.Raise()`: Called by backend/gameplay logic when an event occurs.
- `NotificationChannel.Clear()`: Called by UI logic when the player acknowledges the notification (e.g., by opening the relevant window or clicking the item).
