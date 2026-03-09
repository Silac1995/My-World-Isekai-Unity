---
name: global
description: Global project rules, C# coding style, optimization, and best practices specific to the 2D/3D Unity environment.
---

# Global Project Rules

This skill contains the fundamental rules and best practices to systematically apply for any development in this Unity project.

## When to use this skill
- **Always**: When writing, modifying, or reviewing any C# script in this project.
- Before proposing a new architecture or feature (to ensure it respects the optimization and multiplayer vision).
- When managing coroutines, events, and memory.

## How to use it
Strictly apply the following rules when writing code:

### 1. C# Style and Architecture
- **Private attributes**: Always prefix private attributes with an underscore `_` (e.g., `_skeletonAnimation`).
- **Encapsulation**: Favor private attributes with accessors (`get` properties) or `[SerializeField] private` for the Unity inspector. The use of public attributes should be avoided unless absolutely necessary.

### 2. Game Context
- **3D/2D Hybrid**: The game is developed in Unity within a 3D environment, but uses 2D character sprites (notably Spine). Take 3D/2D interactions into account.
- **Multiplayer**: The game is designed with the objective of being multiplayer. Think about network architecture and avoid singletons or dependencies that would hinder this evolution.

### 3. Optimization and Memory Safety
- **Performance**: Optimization is an absolute priority. Avoid any unnecessary allocation in the `Update` loop and prevent memory leaks.
- **Coroutine Management**:
    - *Never* let a Coroutine run unchecked.
    - Keep a reference to your coroutines. Every `StartCoroutine` should ideally be accompanied by a `StopCoroutine` (or `StopAllCoroutines`) in `OnDisable` or `OnDestroy`.
- **Event Management**:
    - Always unsubscribe from events (C# actions, Unity events, Spine animations) in the `OnDisable` or `OnDestroy` method to avoid memory leaks.
