using System.Collections.Generic;
using UnityEngine;

namespace MWI.Cinematics
{
    [CreateAssetMenu(
        fileName = "NewCinematicScene",
        menuName = "MWI/Cinematics/Scene")]
    public class CinematicSceneSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _sceneId = System.Guid.NewGuid().ToString("N");
        [SerializeField] private string _displayName;

        [Header("Triggering (Phase 2 — wired in by registry; Phase 1 ignores)")]
        [SerializeField] private TriggerAuthority _triggerAuthority = TriggerAuthority.AnyPlayer;
        [SerializeField] private int _priority = 50;

        [Header("Lifecycle (Phase 2 — registry consults these)")]
        [SerializeField] private PlayMode _playMode = PlayMode.OncePerWorld;
        [SerializeField] private AdvanceMode _advanceMode = AdvanceMode.AllMustPress;
        [SerializeField] private float _advanceGraceSec = 5f;

        [Header("Cast")]
        [SerializeField] private List<RoleSlot> _roles = new();

        [Header("Timeline")]
        [SerializeReference] private List<CinematicStep> _steps = new();

        public string  SceneId          => _sceneId;
        public string  DisplayName      => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public TriggerAuthority TriggerAuthority => _triggerAuthority;
        public int     Priority         => _priority;
        public PlayMode  PlayMode       => _playMode;
        public AdvanceMode AdvanceMode  => _advanceMode;
        public float   AdvanceGraceSec  => _advanceGraceSec;
        public IReadOnlyList<RoleSlot>  Roles => _roles;
        public IReadOnlyList<CinematicStep> Steps => _steps;

        // Re-seeds sceneId only when the field is empty (e.g., a designer accidentally
        // cleared it). Does NOT detect Ctrl+D duplicates — the duplicate inherits the
        // original GUID. Phase 2's CinematicRegistry detects and warns on duplicate
        // SceneIds at registry boot (fail-fast); designers should Ctrl+D + manually
        // clear the sceneId field, OR the registry-side check catches it.
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_sceneId))
                _sceneId = System.Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Inspector quick-test entry point. Right-click this asset's header in the
        /// Inspector → "Play in Active Scene". Looks for a player Character in the
        /// active scene to use as <c>TriggeringPlayer</c>; if not found, falls back to
        /// the first Character available. No external scripts (DebugScript / DevModeManager
        /// modules) needed for Phase 1 verification.
        ///
        /// Phase 4 will replace this with a "Test" button on the Cinematic Scene Editor
        /// window plus a DevCinematicModule tab in the dev panel.
        /// </summary>
        [ContextMenu("Play in Active Scene")]
        private void Editor_PlayInActiveScene()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> '{name}': enter Play mode first — cinematics need live Character instances.");
                return;
            }

            // Prefer a player avatar so the gating semantics (IsCinematicActor blocks
            // PlayerController input) are exercised end-to-end.
            Character triggeringPlayer = null;
            var all = FindObjectsByType<Character>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsPlayer())
                {
                    triggeringPlayer = all[i];
                    break;
                }
            }
            // Fallback: first character of any kind.
            if (triggeringPlayer == null && all.Length > 0)
                triggeringPlayer = all[0];

            if (triggeringPlayer == null)
            {
                Debug.LogError($"<color=red>[Cinematic]</color> '{name}': no Character found in active scene to use as TriggeringPlayer.");
                return;
            }

            Debug.Log($"<color=cyan>[Cinematic]</color> '{name}': inspector-driven play. Triggering player = '{triggeringPlayer.CharacterName}'.");
            Cinematics.TryPlay(this, triggeringPlayer);
        }
    }
}
