using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Runtime context threaded through every step callback. Server-side only.
    /// Steps read from this; they should not mutate it post-OnEnter.
    /// </summary>
    public class CinematicContext
    {
        public CinematicSceneSO    Scene             { get; internal set; }
        public CinematicDirector   Director          { get; internal set; }
        public Character           TriggeringPlayer  { get; internal set; }
        public Character           OtherParticipant  { get; internal set; }   // null for surfaces with no second party
        public Vector3             TriggerOrigin     { get; internal set; }
        public float               StartTimeSim      { get; internal set; }

        // Mutable during scene start (role resolution); read-only after
        public Dictionary<ActorRoleId, Character> BoundRoles    { get; } = new();
        public Dictionary<string, GameObject>     BoundObjects  { get; } = new();
        public List<Character>                    ParticipatingPlayers { get; } = new();

        /// <summary>
        /// Resolve a role to its bound Character.
        /// Throws if the role is required and missing; returns null if optional and missing.
        /// </summary>
        public Character GetActor(ActorRoleId id)
        {
            if (BoundRoles.TryGetValue(id, out var c)) return c;

            // Look up role definition to know if optional
            if (Scene != null)
            {
                foreach (var slot in Scene.Roles)
                {
                    if (slot.RoleId == id)
                    {
                        if (slot.IsOptional) return null;
                        Debug.LogError($"<color=red>[Cinematic]</color> Required role '{id}' is unbound on scene '{Scene.SceneId}'.");
                        return null;
                    }
                }
            }

            Debug.LogWarning($"<color=yellow>[Cinematic]</color> Role '{id}' is not declared on scene '{Scene?.SceneId}'.");
            return null;
        }

        public GameObject GetObject(string key) =>
            BoundObjects.TryGetValue(key, out var go) ? go : null;
    }
}
