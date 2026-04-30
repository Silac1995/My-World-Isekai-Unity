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
    }
}
