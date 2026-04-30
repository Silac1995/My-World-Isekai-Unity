using UnityEngine;

// We're inside namespace MWI.Cinematics, so `Time` resolves to the sibling MWI.Time
// namespace before reaching UnityEngine.Time. Aliasing avoids fully-qualifying every call.
using UTime = UnityEngine.Time;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class WaitStep : CinematicStep
    {
        [SerializeField] private float _durationSec = 1f;

        private float _endTimeSim;

        public override void OnEnter(CinematicContext ctx)
        {
            _endTimeSim = UTime.time + Mathf.Max(0f, _durationSec);
            Debug.Log($"<color=cyan>[Cinematic]</color> WaitStep entered — will complete at sim time {_endTimeSim:F2} (duration {_durationSec:F2}s).");
        }

        public override bool IsComplete(CinematicContext ctx) => UTime.time >= _endTimeSim;
    }
}
