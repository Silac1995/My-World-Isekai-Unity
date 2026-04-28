// Assets/Scripts/DayNightCycle/TimeSkipController.cs
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Time
{
    /// <summary>
    /// Server-authoritative singleton that owns the time-skip lifecycle:
    /// hibernate-skip-wake. Coexists with <see cref="GameSpeedController"/> —
    /// that one runs live simulation faster; this one freezes the active map
    /// and runs <see cref="MacroSimulator.SimulateOneHour"/> per hour.
    ///
    /// v1: single-player only (gated on ConnectedClients.Count == 1).
    /// v2: replace gate with "all connected players are simultaneously in a bed slot."
    /// </summary>
    public class TimeSkipController : NetworkBehaviour
    {
        public static TimeSkipController Instance { get; private set; }

        public const int MaxHours = 168;

        public bool IsSkipping { get; private set; }

        /// <summary>Fires on the server at the start of each skipped hour. Argument is the elapsed hours so far (1..hoursToSkip).</summary>
        public event System.Action<int, int> OnSkipHourTick;
        /// <summary>Fires on the server when the skip ends (completed or aborted).</summary>
        public event System.Action OnSkipEnded;
        /// <summary>Fires on the server when a skip starts.</summary>
        public event System.Action<int> OnSkipStarted;

        private bool _aborted;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Defensive cleanup if the controller is destroyed mid-skip (scene unload,
        /// portal transition, etc.). Unity stops the coroutine automatically; we
        /// just need to fire OnSkipEnded so subscribers (UI overlay) can hide.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (IsSkipping)
            {
                IsSkipping = false;
                OnSkipEnded?.Invoke();
            }
        }

        /// <summary>
        /// Main entry point. Server-only. Returns true if the skip was successfully started.
        /// </summary>
        public bool RequestSkip(int hours)
        {
            if (!IsServer)
            {
                Debug.LogWarning("<color=orange>[TimeSkip]</color> RequestSkip called on non-server peer. Ignored.");
                return false;
            }
            if (IsSkipping)
            {
                Debug.LogWarning("<color=orange>[TimeSkip]</color> RequestSkip rejected — already skipping.");
                return false;
            }
            if (hours < 1 || hours > MaxHours)
            {
                Debug.LogWarning($"<color=orange>[TimeSkip]</color> RequestSkip rejected — hours={hours} outside [1, {MaxHours}].");
                return false;
            }
            int connectedCount = NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClients.Count : 1;
            if (connectedCount > 1)
            {
                Debug.LogWarning($"<color=orange>[TimeSkip]</color> RequestSkip rejected — multiplayer not supported in v1 (connected={connectedCount}).");
                return false;
            }
            if (TimeManager.Instance == null)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> RequestSkip rejected — TimeManager.Instance is null.");
                return false;
            }

            StartCoroutine(RunSkip(hours));
            return true;
        }

        public void RequestAbort()
        {
            if (!IsServer || !IsSkipping) return;
            _aborted = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("<color=cyan>[TimeSkip]</color> RequestAbort received — loop will exit at next iteration.");
#endif
        }

        private IEnumerator RunSkip(int hours)
        {
            IsSkipping = true;
            _aborted = false;

            // Freeze Unity time during the skip so TimeManager.ProgressTime cannot
            // double-advance the clock on top of our per-hour AdvanceOneHour calls
            // (and so live NPC AI / coroutines using scaled time pause cleanly).
            // Capture and restore the previous timeScale so we don't fight
            // GameSpeedController's own value.
            float savedTimeScale = UnityEngine.Time.timeScale;
            UnityEngine.Time.timeScale = 0f;

            OnSkipStarted?.Invoke(hours);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"<color=cyan>[TimeSkip]</color> Skip starting: {hours} in-game hours.");
#endif

            // 1. Snapshot the active map(s) the player(s) are on.
            //    v1 single-player: there is exactly one player and one active map.
            MapController activeMap = ResolveActivePlayerMap();
            if (activeMap == null)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> Could not resolve active player map. Aborting.");
                UnityEngine.Time.timeScale = savedTimeScale;
                IsSkipping = false;
                OnSkipEnded?.Invoke();
                yield break;
            }

            // 2. EnterSkipMode — hibernate the active map and freeze player(s).
            //    Player.EnterSleep is called by the bed BEFORE RequestSkip in the bed flow.
            //    For dev / chat commands the player is NOT in a bed; we still freeze them
            //    in place by calling EnterSleep with their own current transform.
            Character[] players = ResolveLocalPlayers();
            foreach (var player in players)
            {
                if (player != null && !player.IsSleeping)
                    player.EnterSleep(player.transform);  // freeze in place; no anchor snap
            }
            activeMap.HibernateForSkip();

            // 3. Per-hour loop.
            int hoursElapsed = 0;
            while (hoursElapsed < hours)
            {
                if (_aborted) break;

                // Player-death auto-abort
                bool anyDead = false;
                foreach (var p in players) if (p == null || !p.IsAlive()) { anyDead = true; break; }
                if (anyDead)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log("<color=cyan>[TimeSkip]</color> Aborting — player dead.");
#endif
                    break;
                }

                int prevHour = TimeManager.Instance.CurrentHour;
                TimeManager.Instance.AdvanceOneHour();

                MacroSimulator.SimulateOneHour(
                    activeMap.HibernationData,
                    TimeManager.Instance.CurrentDay,
                    TimeManager.Instance.CurrentTime01,
                    activeMap.JobYields,
                    prevHour);

                hoursElapsed++;
                OnSkipHourTick?.Invoke(hoursElapsed, hours);

                yield return null;  // one frame per hour — lets cancel UI tick + abort flag flip
            }

            // 4. ExitSkipMode — wake the map and unfreeze the player(s).
            //    The map's PendingSkipWake flag suppresses the redundant SimulateCatchUp.
            //    Wrapped in try/catch/finally so a WakeUp exception (e.g., the map
            //    GameObject being destroyed by some side effect of Hibernate) cannot
            //    permanently strand IsSkipping = true and block future skips.
            try
            {
                if (activeMap != null) activeMap.WakeUpFromSkip();
                else Debug.LogWarning("<color=orange>[TimeSkip]</color> activeMap was destroyed during the skip; WakeUpFromSkip skipped. Map will wake naturally on next player approach.");
            }
            catch (System.Exception e)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> Exception during WakeUpFromSkip — falling through to cleanup so future skips remain unblocked.");
                Debug.LogException(e);
            }

            try
            {
                foreach (var player in players)
                {
                    if (player != null && player.IsSleeping) player.ExitSleep();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> Exception during ExitSleep — falling through.");
                Debug.LogException(e);
            }

            // 5. Restore the captured timeScale before save so SaveManager doesn't
            //    inherit a frozen Unity clock, then trigger the post-skip save.
            UnityEngine.Time.timeScale = savedTimeScale;

            try
            {
                if (SaveManager.Instance != null && players.Length > 0 && players[0] != null)
                {
                    SaveManager.Instance.RequestSave(players[0]);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> Exception during RequestSave — falling through.");
                Debug.LogException(e);
            }

            IsSkipping = false;
            OnSkipEnded?.Invoke();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"<color=cyan>[TimeSkip]</color> Skip ended. Hours actually skipped: {hoursElapsed}/{hours}.");
#endif
        }

        private MapController ResolveActivePlayerMap()
        {
            // v1: pick the first MapController whose ActivePlayers list is non-empty.
            var maps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in maps)
            {
                if (m != null && !m.IsHibernating) return m;
            }
            return null;
        }

        private Character[] ResolveLocalPlayers()
        {
            if (NetworkManager.Singleton == null) return new Character[0];
            var list = new System.Collections.Generic.List<Character>();
            foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
            {
                var po = kvp.Value.PlayerObject;
                if (po != null && po.TryGetComponent(out Character c)) list.Add(c);
            }
            return list.ToArray();
        }
    }
}
