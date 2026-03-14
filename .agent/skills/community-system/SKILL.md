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
- **Tracking**: When a character becomes the owner of a building via `CommercialBuilding.SetOwner()`, that building is automatically added to their current community's `ownedBuildings` list.
- **Privileges**: Owner privileges (like bypassing schedules in `JobLogisticsManager`) remain strictly with the individual owner, not the entire community.

### 4. Manual and Global Management
The `CommunityManager` serves as a global registry and visualization tool. Use it for administrative tasks or scripted setups.

```csharp
// Scripted creation (ignoring requirements)
Community newComm = CommunityManager.Instance.CreateNewCommunity(founder, "Ancient Guild");
```

### 2. Managing Membership
Always use the `AddMember` and `RemoveMember` methods to ensure proper synchronization between the `Community` and the character's `CharacterCommunity` component.

```csharp
// Adding a member
community.AddMember(newCharacter);

// Removing a member
community.RemoveMember(leavingCharacter);
```

### 3. Hierarchy
Communities can be nested to create complex social structures.

```csharp
// Nesting a community
parentCommunity.AddSubCommunity(subCommunity);
```

### 4. Territory (Zones)
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

### 5. Community Levels
Communities can evolve using the `CommunityLevel` enum (e.g., `SmallGroup`, `Camp`, `Village`).

```csharp
community.ChangeLevel(CommunityLevel.Village);
```

## Architecture Notes
- **`Community`**: A data class (not a MonoBehavior) that stores membership, leadership, and zones.
- **`CommunityManager`**: A Singleton MonoBehavior that handles global registry and dynamic zone instantiation.
- **`CharacterCommunity`**: The character's component that tracks their current community affiliation.
- **`Zone`**: A physical `BoxCollider` trigger in the world representing community territory.
