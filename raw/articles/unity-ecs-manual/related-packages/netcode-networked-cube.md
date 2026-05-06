---
source_url: http://docs.unity3d.com/Packages/com.unity.netcode@6.6/manual/networked-cube.html
fetched: 2026-05-05
section: related-packages
package: netcode-for-entities
---

# Networked Cube Tutorial - Netcode for Entities 6.6.0

## Overview

This tutorial introduces fundamental concepts for creating client-server based games using Netcode for Entities. It guides developers through setting up networked communication, spawning synchronized game objects, and implementing player input handling.

## Creating an Initial Scene

The foundation involves establishing separate worlds for server and client communication. Create a subscene named "SharedData" to contain shared data:

1. Right-click in the Hierarchy window
2. Select **New Subscene** > **Empty Scene...**
3. Name it "SharedData"

Add a plane to both client and server worlds by right-clicking the SharedData subscene and selecting **3D Object** > **Plane**. Launch the editor and open **Window** > **Entities** > **Hierarchy** to verify both ClientWorld and ServerWorld display the shared plane.

## Establishing Connection

Enable client-server communication using the auto-connect feature. Create `Game.cs`:

```csharp
[UnityEngine.Scripting.Preserve]
public class GameBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        AutoConnectPort = 7979; // Enabled auto connect
        return base.Initialize(defaultWorldName);
    }
}
```

## Server Communication

Once connected, implement the `InGame` concept—marking connections as ready for synchronization. Create `GoInGame.cs` with RPC-based communication:

```csharp
[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation |
                   WorldSystemFilterFlags.ServerSimulation |
                   WorldSystemFilterFlags.ThinClientSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[CreateAfter(typeof(RpcSystem))]
public partial struct SetRpcSystemDynamicAssemblyListSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        SystemAPI.GetSingletonRW<RpcCollection>().ValueRW.DynamicAssemblyList = true;
        state.Enabled = false;
    }
}

public struct GoInGameRequest : IRpcCommand
{
}

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation |
                   WorldSystemFilterFlags.ThinClientSimulation)]
public partial struct GoInGameClientSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<NetworkId>()
            .WithNone<NetworkStreamInGame>();
        state.RequireForUpdate(state.GetEntityQuery(builder));
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (id, entity) in SystemAPI.Query<RefRO<NetworkId>>()
            .WithEntityAccess().WithNone<NetworkStreamInGame>())
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(entity);
            var req = commandBuffer.CreateEntity();
            commandBuffer.AddComponent<GoInGameRequest>(req);
            commandBuffer.AddComponent(req, new SendRpcCommandRequest
            {
                TargetConnection = entity
            });
        }
        commandBuffer.Playback(state.EntityManager);
    }
}

[BurstCompile]
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
public partial struct GoInGameServerSystem : ISystem
{
    private ComponentLookup<NetworkId> networkIdFromEntity;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        var builder = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<GoInGameRequest>()
            .WithAll<ReceiveRpcCommandRequest>();
        state.RequireForUpdate(state.GetEntityQuery(builder));
        networkIdFromEntity = state.GetComponentLookup<NetworkId>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var worldName = state.WorldUnmanaged.Name;
        var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        networkIdFromEntity.Update(ref state);

        foreach (var (reqSrc, reqEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
            .WithAll<GoInGameRequest>().WithEntityAccess())
        {
            commandBuffer.AddComponent<NetworkStreamInGame>(reqSrc.ValueRO.SourceConnection);
            var networkId = networkIdFromEntity[reqSrc.ValueRO.SourceConnection];
            Debug.Log($"'{worldName}' setting connection '{networkId.Value}' to in game");
            commandBuffer.DestroyEntity(reqEntity);
        }
        commandBuffer.Playback(state.EntityManager);
    }
}
```

## Creating Ghost Prefabs

Networked objects are defined as "ghosts." Create a cube prefab:

1. Right-click the scene and select **3D Object** > **Cube**
2. Drag the cube into the Assets folder to create a prefab
3. Delete the scene cube (keep the prefab)

Define the cube component via `CubeAuthoring.cs`:

```csharp
public struct Cube : IComponentData
{
}

[DisallowMultipleComponent]
public class CubeAuthoring : MonoBehaviour
{
    class CubeBaker : Baker<CubeAuthoring>
    {
        public override void Bake(CubeAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Cube>(entity);
        }
    }
}
```

Add the **Ghost Authoring Component** to the prefab and configure:

- Check **Has Owner**
- Change **Default Ghost Mode** to **Owner Predicted**

This ensures Translation and Rotation synchronization, and enables prediction for the owning client.

## Creating a Spawner

Define the spawner component via `CubeSpawnerAuthoring.cs`:

```csharp
public struct CubeSpawner : IComponentData
{
    public Entity Cube;
}

[DisallowMultipleComponent]
public class CubeSpawnerAuthoring : MonoBehaviour
{
    public GameObject Cube;

    class Baker : Baker<CubeSpawnerAuthoring>
    {
        public override void Bake(CubeSpawnerAuthoring authoring)
        {
            CubeSpawner component = default(CubeSpawner);
            component.Cube = GetEntity(authoring.Cube, TransformUsageFlags.Dynamic);
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, component);
        }
    }
}
```

Setup in the scene:

1. Right-click SharedData and select **Create Empty**
2. Rename to "Spawner" and add **CubeSpawner** component
3. Drag the cube prefab into the spawner's cube field

## Spawning Prefabs

Update `GoInGame.cs` to spawn cubes on client connections. Modify both systems' `OnCreate` methods:

```csharp
state.RequireForUpdate<CubeSpawner>();
```

Update `GoInGameServerSystem.OnUpdate`:

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var prefab = SystemAPI.GetSingleton<CubeSpawner>().Cube;
    state.EntityManager.GetName(prefab, out var prefabName);
    var worldName = new FixedString32Bytes(state.WorldUnmanaged.Name);

    var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
    networkIdFromEntity.Update(ref state);

    foreach (var (reqSrc, reqEntity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>()
        .WithAll<GoInGameRequest>().WithEntityAccess())
    {
        commandBuffer.AddComponent<NetworkStreamInGame>(reqSrc.ValueRO.SourceConnection);
        var networkId = networkIdFromEntity[reqSrc.ValueRO.SourceConnection];

        UnityEngine.Debug.Log($"'{worldName}' setting connection '{networkId.Value}' " +
            $"to in game, spawning a Ghost '{prefabName}' for them!");

        var player = commandBuffer.Instantiate(prefab);
        commandBuffer.SetComponent(player, new GhostOwner
        {
            NetworkId = networkId.Value
        });
        commandBuffer.AppendToBuffer(reqSrc.ValueRO.SourceConnection,
            new LinkedEntityGroup{Value = player});
        commandBuffer.DestroyEntity(reqEntity);
    }
    commandBuffer.Playback(state.EntityManager);
}
```

## Moving the Cube

Implement input handling via `CubeInputAuthoring.cs`:

```csharp
public struct CubeInput : IInputComponentData
{
    public int Horizontal;
    public int Vertical;
}

[DisallowMultipleComponent]
public class CubeInputAuthoring : MonoBehaviour
{
    class CubeInputBaking : Unity.Entities.Baker<CubeInputAuthoring>
    {
        public override void Bake(CubeInputAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<CubeInput>(entity);
        }
    }
}

[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial struct SampleCubeInput : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<CubeSpawner>();
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach (var playerInput in SystemAPI.Query<RefRW<CubeInput>>()
            .WithAll<GhostOwnerIsLocal>())
        {
            playerInput.ValueRW = default;
            if (Input.GetKey("left"))
                playerInput.ValueRW.Horizontal -= 1;
            if (Input.GetKey("right"))
                playerInput.ValueRW.Horizontal += 1;
            if (Input.GetKey("down"))
                playerInput.ValueRW.Vertical -= 1;
            if (Input.GetKey("up"))
                playerInput.ValueRW.Vertical += 1;
        }
    }
}
```

Add `CubeInputAuthoring` to the cube prefab. Create `CubeMovementSystem.cs`:

```csharp
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[BurstCompile]
public partial struct CubeMovementSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var speed = SystemAPI.Time.DeltaTime * 4;
        foreach (var (input, trans) in SystemAPI.Query<RefRO<CubeInput>,
            RefRW<LocalTransform>>().WithAll<Simulate>())
        {
            var moveInput = new float2(input.ValueRO.Horizontal,
                input.ValueRO.Vertical);
            moveInput = math.normalizesafe(moveInput) * speed;
            trans.ValueRW.Position += new float3(moveInput.x, 0, moveInput.y);
        }
    }
}
```

## Testing

Open **Multiplayer** > **PlayMode Tools** and set **PlayMode Type** to **Client & Server**. Enter Play Mode and use arrow keys to move the cube.

## Standalone Build Testing

To test with a standalone build:

1. Verify **Project Settings** > **Entities** > **Build** > **NetCode Client Target** is set to ClientAndServer
2. Create a development build
3. Open **Multiplayer** > **PlayMode Tools** in Editor
4. Set **PlayMode Type** to Client and **Auto Connect Port** to 7979
5. Enter Play Mode

Both cubes should now be visible and controllable.

---

## Outgoing Hyperlinks

- `installation.html` — Installation Guide
- `client-server-worlds.html` — Client-Server Worlds
- `https://docs.unity3d.com/Packages/com.unity.entities@latest` — Entities Package
- `network-connection.html` — Network Connection
- `synchronization.html` — Synchronization
- `ghost-snapshots.html` — Ghost Snapshots
- `https://docs.unity3d.com/Packages/com.unity.entities@1.0/api/Unity.Entities.SystemState.RequireForUpdate.html` — SystemState.RequireForUpdate
