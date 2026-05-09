using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MWI.UI.Loading
{
    /// <summary>
    /// Short-lived MonoBehaviour that observes Netcode-for-GameObjects connection events
    /// and pushes stage updates into <see cref="LoadingOverlay"/>. Created by
    /// <c>GameSessionManager.JoinMultiplayer()</c> immediately before <c>StartClient()</c>;
    /// self-destructs on connect, disconnect, or cancel.
    ///
    /// Stage map (matches docs/superpowers/specs/2026-04-25-loading-overlay-design.md §5):
    ///   1. OnClientStarted              → "Connecting to host…"        0.10
    ///   2. (after StartClient returned) → "Awaiting host approval…"    0.25
    ///   3. SceneEvent.Load              → "Loading scene: {name}…"     0.40
    ///   4. SceneEvent.Synchronize       → "Synchronizing world…"       0.60→0.90 (asymptotic)
    ///   5. SceneEvent.SynchronizeComplete → "Finalizing…"              0.95
    ///   6. OnClientConnectedCallback    → Hide() + self-destruct
    ///   7. OnClientDisconnectCallback (pre-connect) → ShowFailure
    ///
    /// Stage 4 polls <c>NetworkManager.SpawnManager.SpawnedObjectsList.Count</c> at 10 Hz
    /// (unscaled time) and pushes <c>SetDetail("{n} entities loaded")</c>. The bar fill
    /// follows <c>0.60 + 0.30 * n / (n + 50)</c>.
    /// </summary>
    public class NetworkConnectionLoadingDriver : MonoBehaviour
    {
        private const string MainMenuSceneName = "MainMenuScene";
        private const float SpawnPollIntervalSeconds = 0.1f;

        private bool _connected;
        private bool _inSynchronizeStage;
        private int _spawnBaseline;
        private Coroutine _spawnPollCoroutine;

        private NetworkManager _nm;

        private void OnEnable()
        {
            _nm = NetworkManager.Singleton;
            if (_nm == null)
            {
                Debug.LogError("[NetworkConnectionLoadingDriver] NetworkManager.Singleton is null on enable. Driver will self-destruct.");
                Destroy(gameObject);
                return;
            }

            _nm.OnClientStarted += HandleClientStarted;
            _nm.OnClientConnectedCallback += HandleClientConnected;
            _nm.OnClientDisconnectCallback += HandleClientDisconnect;

            if (_nm.SceneManager != null)
            {
                _nm.SceneManager.OnSceneEvent += HandleSceneEvent;
            }
            else
            {
                // SceneManager is created when StartClient succeeds. Watch for it via a one-shot poll.
                StartCoroutine(WatchForSceneManager());
            }
        }

        private void OnDisable()
        {
            if (_nm != null)
            {
                _nm.OnClientStarted -= HandleClientStarted;
                _nm.OnClientConnectedCallback -= HandleClientConnected;
                _nm.OnClientDisconnectCallback -= HandleClientDisconnect;
                if (_nm.SceneManager != null) _nm.SceneManager.OnSceneEvent -= HandleSceneEvent;
            }
            if (_spawnPollCoroutine != null) { StopCoroutine(_spawnPollCoroutine); _spawnPollCoroutine = null; }
        }

        private IEnumerator WatchForSceneManager()
        {
            // Poll once per frame until SceneManager exists or we self-destruct.
            while (this != null && _nm != null && _nm.SceneManager == null)
            {
                yield return null;
            }
            if (this != null && _nm != null && _nm.SceneManager != null)
            {
                _nm.SceneManager.OnSceneEvent += HandleSceneEvent;
            }
        }

        private void HandleClientStarted()
        {
            var overlay = LoadingOverlay.Instance;
            if (overlay == null) return;
            overlay.SetStage("Connecting to host…", 0.10f);
            // Stage 2 — set the "awaiting approval" text right after the underlying transport handshake.
            // No explicit NGO event for this transition, so we time-fade after one frame.
            StartCoroutine(StepToAwaitingApproval());
        }

        private IEnumerator StepToAwaitingApproval()
        {
            yield return null; // one frame later
            if (_connected) yield break;
            LoadingOverlay.Instance?.SetStage("Awaiting host approval…", 0.25f);
        }

        private void HandleSceneEvent(SceneEvent ev)
        {
            // Only react to events targeted at the local client.
            // The synchronize sequence is fired against the connecting client's id.
            var overlay = LoadingOverlay.Instance;
            if (overlay == null || _connected) return;

            switch (ev.SceneEventType)
            {
                case SceneEventType.Load:
                    overlay.SetStage($"Loading scene: {ev.SceneName}…", 0.40f);
                    break;

                case SceneEventType.Synchronize:
                    EnterSynchronizeStage();
                    break;

                case SceneEventType.SynchronizeComplete:
                    ExitSynchronizeStage();
                    overlay.SetStage("Finalizing…", 0.95f);
                    break;
            }
        }

        private void EnterSynchronizeStage()
        {
            _inSynchronizeStage = true;
            _spawnBaseline = _nm != null && _nm.SpawnManager != null
                ? _nm.SpawnManager.SpawnedObjectsList.Count
                : 0;

            var overlay = LoadingOverlay.Instance;
            if (overlay != null)
            {
                overlay.SetStage("Synchronizing world…", 0.60f);
                overlay.SetDetail("0 entities loaded");
            }

            if (_spawnPollCoroutine != null) StopCoroutine(_spawnPollCoroutine);
            _spawnPollCoroutine = StartCoroutine(PollSpawnCount());
        }

        private void ExitSynchronizeStage()
        {
            _inSynchronizeStage = false;
            if (_spawnPollCoroutine != null) { StopCoroutine(_spawnPollCoroutine); _spawnPollCoroutine = null; }
            LoadingOverlay.Instance?.SetDetail(string.Empty);
        }

        private IEnumerator PollSpawnCount()
        {
            var wait = new WaitForSecondsRealtime(SpawnPollIntervalSeconds);
            while (_inSynchronizeStage && this != null && _nm != null && _nm.SpawnManager != null)
            {
                int count = _nm.SpawnManager.SpawnedObjectsList.Count - _spawnBaseline;
                if (count < 0) count = 0;

                float fill = 0.60f + 0.30f * (count / (count + 50f));
                if (fill > 0.90f) fill = 0.90f;

                var overlay = LoadingOverlay.Instance;
                if (overlay != null)
                {
                    overlay.SetStage("Synchronizing world…", fill);
                    overlay.SetDetail($"{count} entities loaded");
                }
                yield return wait;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (_nm == null || clientId != _nm.LocalClientId) return;
            _connected = true;
            LoadingOverlay.Instance?.Hide();
            Destroy(gameObject);
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            // Only react to OUR client id disconnecting (or the unknown-pre-connect 0).
            if (_nm != null && clientId != _nm.LocalClientId && _connected) return;
            if (_connected)
            {
                // Disconnect AFTER a successful connect — overlay was already hidden, do nothing.
                Destroy(gameObject);
                return;
            }

            string reason = _nm != null && !string.IsNullOrEmpty(_nm.DisconnectReason)
                ? _nm.DisconnectReason
                : "lost connection to host";

            var overlay = LoadingOverlay.Instance;
            if (overlay != null)
            {
                overlay.SetCancelHandler(BackToMainMenu, cancelDelaySeconds: 0f);
                overlay.ShowFailure(reason);
            }
            // Do NOT self-destruct yet — let the user click "Back to main menu" to leave.
        }

        public void RegisterCancelHandler()
        {
            // Called by GameSessionManager.JoinMultiplayer right after instantiating us.
            LoadingOverlay.Instance?.SetCancelHandler(CancelJoin, cancelDelaySeconds: 10f);
        }

        private void CancelJoin()
        {
            if (_nm != null && _nm.IsListening) _nm.Shutdown();
            BackToMainMenu();
        }

        private void BackToMainMenu()
        {
            LoadingOverlay.Instance?.Hide();
            Destroy(gameObject);
            SceneManager.LoadScene(MainMenuSceneName);
        }
    }
}
