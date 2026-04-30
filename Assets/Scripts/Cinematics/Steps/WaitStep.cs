using UnityEngine;

namespace MWI.Cinematics
{
    [System.Serializable]
    public class WaitStep : CinematicStep
    {
        [SerializeField] private float _durationSec = 1f;

        private float _endTimeSim;

        public override void OnEnter(CinematicContext ctx)
        {
            _endTimeSim = Time.time + Mathf.Max(0f, _durationSec);
            Debug.Log($"<color=cyan>[Cinematic]</color> WaitStep entered — will complete at sim time {_endTimeSim:F2} (duration {_durationSec:F2}s).");
        }

        public override bool IsComplete(CinematicContext ctx) => Time.time >= _endTimeSim;
    }
}
