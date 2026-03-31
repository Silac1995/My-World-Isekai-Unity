using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MWI.UI
{
    /// <summary>
    /// In-game pause menu toggled by ESC.
    /// Pauses simulation in solo sessions (no other player clients).
    /// Overlay-only in multiplayer.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject _menuPanel;
        [SerializeField] private Button _returnToMainMenuButton;

        [Header("Settings")]
        [SerializeField] private float _fadeDuration = 0.5f;
        [SerializeField] private string _mainMenuSceneName = "MainMenuScene";

        private Coroutine _returnCoroutine;
        private bool _didPauseSimulation;

        public bool IsOpen => _menuPanel != null && _menuPanel.activeSelf;

        private void Awake()
        {
            if (_returnToMainMenuButton != null)
            {
                _returnToMainMenuButton.onClick.AddListener(OnReturnToMainMenuClicked);
            }

            // Ensure menu starts closed
            if (_menuPanel != null)
            {
                _menuPanel.SetActive(false);
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // Don't process while return-to-menu transition is running
            if (_returnCoroutine != null) return;

            // Don't process if PlayerUI isn't initialized (no local player)
            if (PlayerUI.Instance == null || !PlayerUI.Instance.IsInitialized) return;

            // Don't open menu if a placement mode is active — ESC should cancel placement instead
            if (IsPlacementActive()) return;

            Toggle();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (_menuPanel == null) return;

            _menuPanel.SetActive(true);

            // Pause simulation in solo sessions
            if (IsSoloSession())
            {
                var speedController = MWI.Time.GameSpeedController.Instance;
                if (speedController != null)
                {
                    speedController.RequestSpeedChange(0f);
                    _didPauseSimulation = true;
                }
            }
        }

        public void Close()
        {
            if (_menuPanel == null) return;

            _menuPanel.SetActive(false);

            ResumeIfPaused();
        }

        private void OnReturnToMainMenuClicked()
        {
            if (_returnCoroutine != null) return; // Already in progress
            _returnCoroutine = StartCoroutine(ReturnToMainMenuCoroutine());
        }

        private IEnumerator ReturnToMainMenuCoroutine()
        {
            // Step 1: Resume simulation if we paused it
            ResumeIfPaused();

            // Step 2: Fade to black
            if (ScreenFadeManager.Instance != null)
            {
                ScreenFadeManager.Instance.FadeOut(_fadeDuration);
                yield return new WaitForSecondsRealtime(_fadeDuration + 0.05f);
            }

            // Step 3: Shutdown network
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }

            // Step 3b: Reset callbacks (shutdown clears them)
            if (GameSessionManager.Instance != null)
                GameSessionManager.Instance.ResetCallbacks();

            // Step 4: Clear launch parameters
            if (GameLauncher.Instance != null)
            {
                GameLauncher.Instance.ClearLaunchParameters();
            }

            // Step 5: Start fade-in before loading (ScreenFadeManager is DontDestroyOnLoad,
            // so its coroutine survives the scene transition)
            if (ScreenFadeManager.Instance != null)
            {
                ScreenFadeManager.Instance.FadeIn(_fadeDuration);
            }

            // Step 6: Load main menu scene
            SceneManager.LoadScene(_mainMenuSceneName);
        }

        private void ResumeIfPaused()
        {
            if (!_didPauseSimulation) return;

            var speedController = MWI.Time.GameSpeedController.Instance;
            if (speedController != null)
            {
                speedController.RequestSpeedChange(1f);
            }

            _didPauseSimulation = false;
        }

        /// <summary>
        /// Returns true if the local player is in building or furniture placement mode.
        /// ESC should cancel placement, not open the pause menu.
        /// </summary>
        private bool IsPlacementActive()
        {
            var character = PlayerUI.Instance?.CharacterComponent;
            if (character == null) return false;

            var buildingPlacement = character.PlacementManager;
            if (buildingPlacement != null && buildingPlacement.IsPlacementActive)
                return true;

            var furniturePlacement = character.FurniturePlacementManager;
            if (furniturePlacement != null && furniturePlacement.IsPlacementActive)
                return true;

            return false;
        }

        /// <summary>
        /// Solo session = host/server with no other player clients connected.
        /// ConnectedClientsList is server-only state, but in solo the local player IS the host.
        /// </summary>
        private bool IsSoloSession()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return false;

            return nm.ConnectedClientsList.Count <= 1;
        }

        private void OnDestroy()
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }

            if (_returnToMainMenuButton != null)
            {
                _returnToMainMenuButton.onClick.RemoveListener(OnReturnToMainMenuClicked);
            }

            // Safety: resume simulation if we're destroyed while paused
            ResumeIfPaused();
        }
    }
}
