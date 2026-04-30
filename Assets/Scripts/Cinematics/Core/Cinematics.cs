using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Public entry point. Phase 1 — server-only, no eligibility checks (those land in Phase 2's
    /// CinematicRegistry). Phase 1's TryPlay binds roles, sets up the context, spawns a director,
    /// and runs the scene.
    ///
    /// Phase 2 callers will go through CinematicRegistry.TryStart for eligibility + PlayMode +
    /// authority gating. Phase 1 leaves Cinematics.TryPlay as the bypass / debug entry point.
    /// </summary>
    public static class Cinematics
    {
        private const string DirectorContainerName = "CinematicDirectors";

        /// <summary>
        /// Trigger a cinematic scene. Phase 1: server-side only (or solo / host).
        /// Phase 2's TryPlay will route through CinematicRegistry for eligibility + PlayMode checks.
        /// </summary>
        /// <returns>true if the scene started; false on missing scene, missing player, or unbindable required roles.</returns>
        public static bool TryPlay(
            CinematicSceneSO scene,
            Character triggeringPlayer,
            Character otherParticipant = null)
        {
            if (scene == null)
            {
                Debug.LogError("<color=red>[Cinematic]</color> Cinematics.TryPlay: scene is null.");
                return false;
            }
            if (triggeringPlayer == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> Cinematics.TryPlay: triggeringPlayer is null (scene='{scene.SceneId}').");
                return false;
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> Cinematics.TryPlay: starting scene '{scene.SceneId}' triggered by '{triggeringPlayer.CharacterName}'.");

            var ctx = new CinematicContext
            {
                TriggeringPlayer = triggeringPlayer,
                OtherParticipant = otherParticipant,
                TriggerOrigin    = triggeringPlayer.transform.position,
            };

            // Resolve roles. Required roles whose selectors return null hard-fail.
            // Optional roles silently skip — steps targeting them check at runtime.
            foreach (var slot in scene.Roles)
            {
                if (slot.Selector == null)
                {
                    if (slot.IsOptional) continue;
                    Debug.LogError($"<color=red>[Cinematic]</color> Required role '{slot.RoleId}' has no selector assigned on scene '{scene.SceneId}'. Aborting.");
                    return false;
                }

                Character bound = null;
                try
                {
                    bound = slot.Selector.Resolve(ctx);
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    Debug.LogError($"<color=red>[Cinematic]</color> Selector '{slot.Selector.name}' for role '{slot.RoleId}' threw — treating as unbound.");
                }

                if (bound == null)
                {
                    if (slot.IsOptional)
                    {
                        Debug.LogWarning($"<color=yellow>[Cinematic]</color> Optional role '{slot.RoleId}' did not bind on scene '{scene.SceneId}'. Continuing.");
                        continue;
                    }
                    Debug.LogError($"<color=red>[Cinematic]</color> Required role '{slot.RoleId}' could not be bound on scene '{scene.SceneId}'. Aborting.");
                    return false;
                }

                ctx.BoundRoles[slot.RoleId] = bound;
                if (bound.IsPlayer()) ctx.ParticipatingPlayers.Add(bound);
            }

            // Mark all bound actors as cinematic actors (combat / input gates start respecting them).
            foreach (var kvp in ctx.BoundRoles)
            {
                kvp.Value.CharacterCinematicState?.MarkActiveActor(scene.SceneId, kvp.Key.Value);
            }

            // Spawn the director under a shared container GameObject for hierarchy hygiene.
            var container = GameObject.Find(DirectorContainerName);
            if (container == null) container = new GameObject(DirectorContainerName);

            var directorGo = new GameObject($"Director_{scene.SceneId}");
            directorGo.transform.SetParent(container.transform);
            var director = directorGo.AddComponent<CinematicDirector>();
            director.Initialize(scene, ctx);
            director.RunScene();

            return true;
        }
    }
}
