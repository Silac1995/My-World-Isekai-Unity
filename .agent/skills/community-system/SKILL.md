---
name: community-system
description: Manages the social and territorial structure of NPCs, including hierarchy, membership, and zones.
---

# Community System

The Community System handles the social grouping and territorial management of characters in the world. It allows for hierarchical structures (parent and sub-communities) and defines physical zones belonging to a community.

## When to use this skill
- When creating or dissolving a social group (village, guild, camp).
- When adding or removing a character from a community.
- When establishing physical territories (Zones) for a community.
- When querying community hierarchy or membership.

## How to use it

### 1. Founding a Community
Founding is an autonomous process managed by the character's `CharacterCommunity` component. It typically triggers during social interactions or specific world events.

**Requirements**:
- **Trait**: The character must have a behavioral trait where `canCreateCommunity` is `true`.
- **Social**: The character must have at least **4 friends** (checked via `CharacterRelation.GetFriendCount()`).
- **Leadership**: The character must not already be the leader of a community.

**Result**:
A new community of level `SmallGroup` is created. If the founder already belongs to a community, the new one automatically becomes a **sub-community** of their current one.

```csharp
// The entry point for founding logic (handles both independent and sub-communities)
characterCommunity.CheckAndCreateCommunity();
```

### 2. Community Hierarchy
Communities can be nested infinitely to form complex social structures (Kingdoms > Duchies > Villages).
- **Direct Hierarchy**: A parent community only tracks its immediate direct sub-communities.
- **Independence**: A sub-community can break free from its parent circle at any time.

```csharp
// Declaring independence (typically triggered by the leader)
community.DeclareIndependence();
```

### 3. Community Assets & Building Ownership
While a `CommercialBuilding` is strictly owned by an **individual** character (`Character _owner`), the community automatically tracks the assets of its members.
*   **Tracking**: When a character becomes the owner of a building via `CommercialBuilding.SetOwner()`, that building is automatically added to their current community's `ownedBuildings` list.
*   **Privileges**: Owner privileges (like bypassing schedules in `JobLogisticsManager`) remain strictly with the individual owner, not the entire community.

### 4. Community Growth & Leader Blueprints
City growth is fundamentally tied to the **Community Leader**.
*   **Offline / Macro-Simulation**: The `MacroSimulator` extracts the Leader's `CharacterBlueprints.UnlockedBuildingIds`. It compares this knowledge against the `WorldSettingsData.BuildingRegistry`.
*   **Priority Filtering**: The simulator only constructs buildings that the Leader knows how to build, filtering out those already present in the community. It selects the next building to construct based on the `CommunityPriority` defined in the registry.
*   This ensures that a community's technological progression is intimately linked to the survival and knowledge of its leader, rather than arbitrary global unlocks.

### 5. Community Leadership and Blueprints
A community tracks its recognized leader via `CommunityData.LeaderNpcId`. The leader has unique authority over the evolution and workforce of the community.

**Blueprint Knowledge (`CharacterBlueprints.cs`)**:
Every character can contain a `CharacterBlueprints` component, storing a list of `PrefabId` strings representing their construction knowledge.
- During city growth, the system checks the Leader's `UnlockedBuildingIds`.
- It cross-references this with the `BuildingRegistry` in `WorldSettingsData`, which defines a `CommunityPriority` for each building.
- The community will autonomously grow by selecting the highest-priority missing building that the Leader knows how to build.
- This knowledge persists during map hibernation by serializing the IDs into `HibernatedNPCData`.

**Imposing Jobs**:
The recognized leader can unilaterally bypass individual character schedules to assign work.
```csharp
// Forcefully assigning a job to a citizen, bypassing their own schedule decisions
CommunityTracker.Instance.ImposeJobOnCitizen(mapId, leaderId, citizen, job, building);
```

### 6. Manual and Global Management
The `CommunityManager` serves as a global registry and visualization tool. Use it for administrative tasks or scripted setups.

```csharp
// Scripted creation (ignoring requirements)
Community newComm = CommunityManager.Instance.CreateNewCommunity(founder, "Ancient Guild");
```

### 7. Managing Membership
Always use the `AddMember` and `RemoveMember` methods to ensure proper synchronization between the `Community` and the character's `CharacterCommunity` component.

```csharp
// Adding a member
community.AddMember(newCharacter);

// Removing a member
community.RemoveMember(leavingCharacter);
```

### 8. Hierarchy
Communities can be nested to create complex social structures.

```csharp
// Nesting a community
parentCommunity.AddSubCommunity(subCommunity);
```

### 9. Territory (Zones)
Communities "own" physical space via the `Zone` system. Zones are instantiated and registered through the manager.

```csharp
// Establishing a camp zone
Zone campZone = CommunityManager.Instance.EstablishCommunityZone(
    community, 
    spawnPosition, 
    ZoneType.Camp, 
    "Main Camp"
);
```

### 10. Community Levels
Communities can evolve using the `CommunityLevel` enum (e.g., `SmallGroup`, `Camp`, `Village`).

```csharp
community.ChangeLevel(CommunityLevel.Village);
```

## Architecture Notes
- **`Community`**: A data class (not a MonoBehavior) that stores membership, leadership, and zones.
- **`CommunityManager`**: A Singleton MonoBehavior that handles global registry and dynamic zone instantiation.
- **`CharacterCommunity`**: The character's component that tracks their current community affiliation.
- **`Zone`**: A physical `BoxCollider` trigger in the world representing community territory.
