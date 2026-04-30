using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Per-character cinematic state. Holds the active-actor flag (Phase 1: local bool;
    /// Phase 2 promotes to <c>NetworkVariable&lt;bool&gt;</c>), the played + pending scene-ID
    /// lists, and the active scene/role identifiers.
    ///
    /// Read by:
    ///   - <c>CharacterCombat</c> → skip damage when <c>IsCinematicActor</c> is true.
    ///   - <c>CharacterAI</c> BT → yield the BT root while bound (Phase 2).
    ///   - <c>CharacterInteraction</c> → block external Talk/Insult while bound (Phase 2).
    ///   - <c>PlayerController</c> → block movement/combat input while bound.
    ///
    /// Written by <c>CinematicDirector</c> (server-side) at scene start and end/abort.
    ///
    /// Phase 2 will add <c>ICharacterSaveData&lt;CinematicHistorySaveData&gt;</c> so the
    /// played/pending lists persist into character profiles (players) and
    /// <c>HibernatedNPCData.ProfileData</c> (NPCs).
    /// </summary>
    public class CharacterCinematicState : CharacterSystem
    {
        // ── Active-actor flag (Phase 1 = local bool; Phase 2 promotes to NetworkVariable<bool>) ──
        private bool _isCinematicActor;
        private string _activeRoleId;
        private string _activeSceneId;

        public bool   IsCinematicActor => _isCinematicActor;
        public string ActiveRoleId     => _activeRoleId;
        public string ActiveSceneId    => _activeSceneId;

        // ── Per-character scene history (Phase 1 = in-memory; Phase 2 adds ICharacterSaveData<T>) ──
        private readonly HashSet<string> _playedSceneIds  = new();
        private readonly HashSet<string> _pendingSceneIds = new();

        public IReadOnlyCollection<string> GetPlayedScenes()  => _playedSceneIds;
        public IReadOnlyCollection<string> GetPendingScenes() => _pendingSceneIds;
        public bool HasPlayedScene(string sceneId) => !string.IsNullOrEmpty(sceneId) && _playedSceneIds.Contains(sceneId);

        // ── Server-side mutators (called by director / registry) ──

        /// <summary>Mark this character as a bound actor in the given scene/role.</summary>
        public void MarkActiveActor(string sceneId, string roleId)
        {
            _isCinematicActor = true;
            _activeSceneId    = sceneId;
            _activeRoleId     = roleId;
            Debug.Log($"<color=cyan>[Cinematic]</color> '{Character?.CharacterName}' is now cinematic actor (scene={sceneId}, role={roleId}).");
        }

        /// <summary>Clear the active-actor flag (called on scene end / abort).</summary>
        public void ClearActiveActor()
        {
            if (!_isCinematicActor) return;
            Debug.Log($"<color=cyan>[Cinematic]</color> '{Character?.CharacterName}' cinematic actor cleared (was scene={_activeSceneId}, role={_activeRoleId}).");
            _isCinematicActor = false;
            _activeSceneId    = null;
            _activeRoleId     = null;
        }

        /// <summary>Record that this character participated in (and completed) the given scene.</summary>
        public void MarkSceneCompleted(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;
            _playedSceneIds.Add(sceneId);
            _pendingSceneIds.Remove(sceneId);
        }

        /// <summary>Add a scene to this character's pending list (e.g. quest reward).</summary>
        public void AddPendingScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;
            _pendingSceneIds.Add(sceneId);
        }

        /// <summary>Remove a scene from this character's pending list.</summary>
        public void RemovePendingScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return;
            _pendingSceneIds.Remove(sceneId);
        }
    }
}
