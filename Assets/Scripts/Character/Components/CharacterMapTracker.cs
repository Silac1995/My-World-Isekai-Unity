using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class CharacterMapTracker : NetworkBehaviour
{
    private Character _character;

    [Tooltip("The ID of the Map/Region this Character is currently in.")]
    public NetworkVariable<FixedString32Bytes> CurrentMapID = new NetworkVariable<FixedString32Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Tooltip("The ID of the Map this Character considers Home.")]
    public NetworkVariable<FixedString32Bytes> HomeMapId = new NetworkVariable<FixedString32Bytes>(
        "",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Tooltip("The specific world position the Character considers Home.")]
    public NetworkVariable<Vector3> HomePosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        _character = GetComponent<Character>();
    }

    /// <summary>
    /// Called by the Client's CharacterMapTransitionAction AFTER it predicts the Warp locally.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    public void RequestTransitionServerRpc(FixedString32Bytes targetMapId, Vector3 targetPosition)
    {
        // 1. Authoritative Warp on Server
        if (TryGetComponent(out CharacterMovement movement))
        {
            movement.Warp(targetPosition);
        }

        // 2. Set new Map state
        SetCurrentMap(targetMapId.ToString());
    }

    /// <summary>
    /// Server-authoritative map assignment. Can be called directly for NPCs.
    /// </summary>
    public void SetCurrentMap(string mapId)
    {
        if (!IsServer) return;
        
        CurrentMapID.Value = mapId;

        // --- RULE 20: Character Decoupling ---
        // TODO: Integrate with ICharacterData to save the LastWorldID / LastCoordinates 
        // to the decoupled JSON/DAT file so if the player disconnects right now, they spawn here later.
        if (_character.IsPlayer())
        {
            Debug.Log($"<color=cyan>[MapTracker]</color> Player {_character.name} transitioned to Map '{mapId}'. (Coordinates Decoupling Placeholder)");
        }
    }
}
