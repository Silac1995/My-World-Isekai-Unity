---
name: world-system
description: Architecture of the World System including Map Hibernation, Spatial Offsets, and Macro-Simulation (Offline Catch-Up) for scaling the game.
---

# WORLD SYSTEM ARCHITECTURE

## 1. Core Philosophy (Spatial Offset)
The World System avoids the overhead of Unity's `NetworkSceneManager` and additive scene loading. Instead, it uses a **Single-Scene Spatial Offset Architecture**.
*   **Regions / Maps** are massive quadrants physically separated by large distances (e.g., Map A at `x=0`, Map B at `x=10000`).
*   **Building Interiors** are placed high in the sky (`y=5000`) or deep underground.
*   **Transitions** between these areas are seamless and are handled by `CharacterMovement.Warp()`.

Because Maps are physically distant, Unity NGO's **Interest Management** naturally filters out network packets across Maps.

## 2. Map Hibernation (Performance)
To support thousands of NPCs and hundreds of Maps locally, we use Map Hibernation.
*   **Activation:** The `MapController` tracks active players. When `PlayerCount == 0`, the Map enters Hibernation.
*   **Phase 1 (Pause):** All NPCs on the Map are serialized into `HibernatedNPCData` (inside `MapSaveData`), extracting only pure logic (Stats, Schedule target, Inventory, Needs) and position. The actual heavy Unity `GameObject` (and `NavMeshAgent`, `Animator`, `NetworkObject`) is DESPAWNED and DESTROYED. The Map is visually dead.
*   **Phase 2 (Wake Up):** When the first player re-enters the dormant Map, the `MapController` immediately calls the `MacroSimulator`.

## 3. Macro-Simulation (Catch-Up Math)
The `MacroSimulator` operates entirely off-screen when a Map wakes up.
*   It calculates the absolute time delta (`DeltaTime = CurrentTime - HibernationTime`).
*   It looks at each `HibernatedNPCData` and mathematically computes what the NPC *would* have done during that gap.
*   **Offline Needs Decay:** It calculates how much time has passed and subtracts that from the serialized `CharacterNeed.CurrentValue` (e.g., Hunger, Social), ensuring NPCs wake up with accurate stat depletion proportionate to the `DeltaTime`.
*   **Persistent Character Progression:** It handles extraction and injection of logic unique to the NPC (e.g. `CharacterBlueprints.UnlockedBuildingIds`) to ensure the city-building capabilities are consistent across Map Hibernation and Wake-Up phases.
*   **Offline City Growth:** Driven entirely by the `Community` Leader's `CharacterBlueprints` knowledge. The Simulator filters the `BuildingRegistry` by the Leader's known `UnlockedBuildingIds`, checks which are missing in the community, and spawns scaffold data respecting the `CommunityPriority`.
*   **Simulation vs Realtime:** Rather than simulating every frame of walking, the MacroSimulator just skips them to the end of their current scheduled task (e.g., if it's 8:00 AM, snap position to Blacksmith Forge).
*   Once simulated, the Server reinstantiates the Prefab at the new position, assigns the updated stats and blueprint data, and calls `Spawn()` to sync with the entering client.

## 4. Map Transitions
Transitions are standardized via `MapTransitionDoor` (exterior-to-exterior) and `BuildingInteriorDoor` (exterior-to-interior).
*   Players interact with a door. `CharacterMapTransitionAction` fades screen via `ScreenFadeManager`, then warps.
*   Cross-NavMesh teleports (building interiors at y=5000) **must** use `CharacterMovement.ForceWarp()`, not `Warp()`. ForceWarp disables the NavMeshAgent, teleports via `transform.position`, then re-enables the agent after 2 frames.
*   The `CharacterMapTracker` is invoked via `RequestTransitionServerRpc` (`FixedString128Bytes` for map IDs). The server resolves interior positions via `ResolveInteriorPosition()` (lazy-spawns if needed) and sends `WarpClientRpc` back if the position changed (ClientNetworkTransform is owner-authoritative).
*   The Server updates the `CurrentMapID` and notifies source/destination MapControllers via `MapController.NotifyPlayerTransition()` for hibernation handoff.
*   See `building-system` SKILL.md for full interior transition architecture.

## 5. Development Rules
*   **Do not rely on `FindObjectOfType`:** Maps hibernate, so GameObjects will completely disappear. Only search serialized data structures (like a hypothetical `WorldManager.HibernatedData`) if trying to locate an off-screen NPC.
*   **No Cross-Map Physics:** A projectile from Map A will never reach Map B. Do not attempt it. Any inter-map effects (e.g. economy shipments) must be calculated purely via Math in the `JobLogisticsManager`, not via physical objects driving across the emptiness.
*   **Character Maps are Authoritative:** Always rely on `CharacterMapTracker.CurrentMapID.Value` to know what map a character belongs to.

## 6. Dynamic City System (Open-World Stamping)
The Physical generation of Maps is entirely data-driven, triggered by NPC clustering behaviors.
*   **WorldSettingsData:** Defines thresholds for communities (ProximityChunkSize, SustainedDays, MinimumPopulation).
*   **CommunityTracker:** A Server-side heartbeat that monitors NPC populations. It evaluates the map state machine: `Roaming Camp -> Settlement -> Established City -> Abandoned City -> Reclaimed`. It triggers physical chunk instantiations upon promotion.
*   **Open-World Stamping:** When a new dynamic map (e.g. Settlement) is formed, the `CommunityTracker` asks the `WorldOffsetAllocator` for a **logical Slot ID** (for saving/data separation) but overrides the physical placement to spawn the `MapController` anchor exactly at the NPC cluster's centroid on the main open-world plane. The NPCs roam freely and are never warped away.
*   **Building Identity:** Dynamic buildings in these maps must not use hardcoded IDs. Each `Building` component generates a unique `NetworkBuildingId` (GUID) on spawn to ensure that multiple instances of the same shop prefab can each link to their own unique interior Map slot.
*   **Abandoned Cities:** Cities never truly dissolve. If population drops to 0 for a prolonged baseline, the city turns "Abandoned", hibernates infinitely at 0 CPU cost, and retains its world slot permanently.

## 7. Offset Allocation (Instanced Cells)
While dynamic open-world content uses centroid-stamping, pure instanced content (like Dungeons, specific Buildings, or isolated narrative maps) uses the `WorldOffsetAllocator`'s physical spatial coordinates.
*   Slots are separated by a constant (e.g., 40,000 units on the X-axis).
*   The Allocator guarantees slot persistence via `WorldSaveManager`.
*   Unused slots are managed via a Lazy Recycling FreeList (30-day cooldown) to prevent stale saves from warping NPCs into the void.

## 8. Debugging & Visualization
*   **MapControllerDebugUI:** A UI component attached to a Canvas within the Map environment. It visualizes the real-time state of the Map (Active vs Hibernating), tracks the exact `ActivePlayers` list, and displays macro-simulation metrics from `HibernationData` (such as the number of hibernated NPCs and items, and the last saved simulation time). This is crucial for verifying that the transition between live simulation and macro-simulation works perfectly off-screen.
