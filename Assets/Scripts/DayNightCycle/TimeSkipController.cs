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
    /// Multiplayer gate: by default, RequestSkip requires every connected
    /// player's Character to have <c>IsSleeping == true</c> (consent flag,
    /// raised when a player enters a bed slot). The dev paths (/timeskip
    /// chat command + dev panel) pass <c>force: true</c> as an admin override
    /// that auto-puts every player into the sleeping state before the skip
    /// runs. Bed / UI flows leave <c>force: false</c> (default).
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
        /// <param name="hours">Number of in-game hours to skip. Must be in [1, MaxHours].</param>
        /// <param name="force">When true, skips the all-players-sleeping gate (admin
        /// override used by the dev panel + /timeskip chat command). The coroutine
        /// will auto-EnterSleep any non-sleeping players before running. When false
        /// (default), every connected player's Character must already have
        /// <c>IsSleeping == true</c> — otherwise the skip is rejected.</param>
        public bool RequestSkip(int hours, bool force = false)
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
            if (TimeManager.Instance == null)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> RequestSkip rejected — TimeManager.Instance is null.");
                return false;
            }

            // Multiplayer consent gate: every connected player must be sleeping.
            // The bed UseSlot path raises Character.IsSleeping = true; the
            // auto-trigger watcher in Update() then auto-fires this method with
            // force=false. Dev paths pass force=true to override.
            if (!force)
            {
                var players = ResolveAllPlayers();
                if (players.Length == 0)
                {
                    Debug.LogWarning("<color=orange>[TimeSkip]</color> RequestSkip rejected — no connected players.");
                    return false;
                }
                foreach (var p in players)
                {
                    if (p == null || !p.IsSleeping)
                    {
                        Debug.LogWarning($"<color=orange>[TimeSkip]</color> RequestSkip rejected — player '{(p != null ? p.CharacterName : "null")}' is not asleep. All connected players must have IsSleeping=true.");
                        return false;
                    }
                }
            }

            StartCoroutine(RunSkip(hours, force));
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

        private IEnumerator RunSkip(int hours, bool force)
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
            Debug.Log($"<color=cyan>[TimeSkip]</color> Skip starting: {hours} in-game hours (force={force}).");
#endif

            // 1. Resolve players and the active map.
            Character[] players = ResolveAllPlayers();
            MapController activeMap = ResolveActivePlayerMap();
            if (activeMap == null)
            {
                Debug.LogError("<color=red>[TimeSkip]</color> Could not resolve active player map. Aborting.");
                UnityEngine.Time.timeScale = savedTimeScale;
                IsSkipping = false;
                OnSkipEnded?.Invoke();
                yield break;
            }

            // 2. Pre-skip checkpoint save — captures the WORLD STATE BEFORE the skip
            //    so a crash, abort, or reload reverts the player to where they were.
            //    Wait until the SaveManager goes Idle before continuing so the snapshot
            //    isn't racing with HibernateForSkip's NPC/building despawns.
            if (SaveManager.Instance != null && players.Length > 0 && players[0] != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("<color=cyan>[TimeSkip]</color> Pre-skip checkpoint save…");
#endif
                SaveManager.Instance.RequestSave(players[0]);
                while (SaveManager.Instance.CurrentState != SaveManager.SaveLoadState.Idle)
                    yield return null;
            }

            // 3. EnterSkipMode — auto-EnterSleep when force=true (admin override),
            //    otherwise the all-sleeping gate has already been checked in
            //    RequestSkip and every player should already be IsSleeping=true.
            if (force)
            {
                foreach (var player in players)
                {
                    if (player != null && !player.IsSleeping)
                        player.EnterSleep(player.transform);  // freeze in place; no anchor snap
                }
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

            // 5. ExitSkipMode — wake the map and unfreeze the player(s).
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

            // 6. Restore the captured timeScale.
            UnityEngine.Time.timeScale = savedTimeScale;

            IsSkipping = false;
            OnSkipEnded?.Invoke();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"<color=cyan>[TimeSkip]</color> Skip ended. Hours actually skipped: {hoursElapsed}/{hours}.");
#endif
        }

        /// <summary>
        /// Server-only watcher: when every connected player's <c>Character.IsSleeping</c>
        /// flips to true and at least one of them has set <c>PendingSkipHours &gt; 0</c>,
        /// auto-fire <see cref="RequestSkip"/> with hours = MIN(each player's target).
        /// "Closest one that the player decided" — whichever player wants the shortest
        /// skip wins; the others are guaranteed at least their requested duration.
        /// </summary>
        private void Update()
        {
            if (!IsServer || IsSkipping) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

            var players = ResolveAllPlayers();
            if (players.Length == 0) return;

            int minHours = int.MaxValue;
            foreach (var p in players)
            {
                if (p == null || !p.IsSleeping) return;  // not all sleeping — skip the auto-trigger
                int h = p.PendingSkipHours;
                if (h > 0 && h < minHours) minHours = h;
            }

            if (minHours == int.MaxValue) return;  // all sleeping but nobody set a target hour yet
            if (minHours < 1 || minHours > MaxHours) return;  // out of range — bed UI should clamp; defensive guard

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"<color=cyan>[TimeSkip]</color> All {players.Length} player(s) sleeping. Auto-firing skip with {minHours}h (closest player target).");
#endif
            RequestSkip(minHours, force: false);
        }

        private MapController ResolveActivePlayerMap()
        {
            // Pick the first non-hibernating MapController. v1 single-player has
            // exactly one; in MP this is the host's "current" map. Future improvement
            // (tracked in optimisation-backlog): route through the local player's
            // CharacterMapTracker.CurrentMapId for the multi-active-map case.
            var maps = UnityEngine.Object.FindObjectsByType<MapController>(FindObjectsSortMode.None);
            foreach (var m in maps)
            {
                if (m != null && !m.IsHibernating) return m;
            }
            return null;
        }

        /// <summary>
        /// Returns the Character on every connected client's PlayerObject. Server-only
        /// callers; clients see an empty array because they don't drive the skip.
        /// </summary>
        private Character[] ResolveAllPlayers()
        {
            if (NetworkManager.Singleton == null) return System.Array.Empty<Character>();
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
