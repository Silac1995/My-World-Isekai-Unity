using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Provides a "Reclaim" interaction option on abandoned NPCs.
/// Only the former party leader who abandoned this NPC can see and use the option.
/// Reclaiming despawns the abandoned NPC on the server so the player can
/// re-invite or re-summon them through normal party flow.
/// </summary>
public class ReclaimNPCInteraction : CharacterSystem, IInteractionProvider
{
    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        var options = new List<InteractionOption>();

        if (!_character.IsAbandoned) return options;

        // Only the original party leader can reclaim this NPC
        if (interactor.CharacterId != _character.FormerPartyLeaderId) return options;

        options.Add(new InteractionOption("Reclaim", () =>
        {
            RequestReclaimServerRpc();
        }));

        return options;
    }

    [Rpc(SendTo.Server)]
    private void RequestReclaimServerRpc(RpcParams rpcParams = default)
    {
        if (!_character.IsAbandoned)
        {
            Debug.LogWarning($"[ReclaimNPCInteraction] {_character.CharacterName} is not abandoned — reclaim rejected.");
            return;
        }

        // Validate that the requesting client owns the former party leader character
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        Character requester = FindRequesterCharacter(senderClientId);

        if (requester == null || requester.CharacterId != _character.FormerPartyLeaderId)
        {
            Debug.LogWarning($"[ReclaimNPCInteraction] Client {senderClientId} is not the former leader — reclaim rejected.");
            return;
        }

        Debug.Log($"[ReclaimNPCInteraction] {requester.CharacterName} reclaimed abandoned NPC '{_character.CharacterName}'.");

        // Clear abandoned state before despawn so the NPC can be re-recruited cleanly
        _character.IsAbandoned = false;
        _character.FormerPartyLeaderId = null;
        _character.FormerPartyLeaderWorldGuid = null;

        // Despawn the abandoned NPC — the player can re-invite through normal party flow
        if (_character.NetworkObject != null && _character.NetworkObject.IsSpawned)
        {
            _character.NetworkObject.Despawn(true);
        }
    }

    /// <summary>
    /// Finds the Character owned by the given client.
    /// </summary>
    private Character FindRequesterCharacter(ulong clientId)
    {
        foreach (Character c in FindObjectsByType<Character>(FindObjectsSortMode.None))
        {
            if (c.NetworkObject != null
                && c.NetworkObject.IsPlayerObject
                && c.NetworkObject.OwnerClientId == clientId)
            {
                return c;
            }
        }
        return null;
    }
}
