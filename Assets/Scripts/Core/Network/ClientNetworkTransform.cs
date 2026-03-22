using Unity.Netcode.Components;
using UnityEngine;

namespace Unity.Netcode.Components
{
    /// <summary>
    /// A NetworkTransform that allows the owner (client) to have authority over its own transform.
    /// This is strictly required when a client-owned object (like the Player) uses a NavMeshAgent
    /// or Rigidbody to move locally, and needs to broadcast those movements to the Server and other clients.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        // This makes the NetworkTransform Owner-authoritative.
        // Whoever owns this NetworkObject (the Server for NPCs, the Client for their specific Player) 
        // will have the absolute right to move it and sync its position.
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
