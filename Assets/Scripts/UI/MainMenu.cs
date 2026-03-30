using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [Header("Menu Buttons")]
    public Button playButton;
    public Button btnStartSolo;
    public Button btnMultiplayer;
    public Button settingsButton;
    public Button creditsButton;
    public Button quitButton;

    [Header("Scene Names")]
    public string gameSceneName = "GameScene";
    public string settingsSceneName = "SettingsScene";
    public string creditsSceneName = "CreditsScene";

    [Header("Panels")]
    [SerializeField] private WorldSelectPanel _worldSelectPanel;

    void Start()
    {
        // Assign methods to buttons if they are defined
        if (playButton != null)
            playButton.onClick.AddListener(() => LoadScene(gameSceneName));

        if (btnStartSolo != null)
            btnStartSolo.onClick.AddListener(StartGame);

        if (btnMultiplayer != null)
            btnMultiplayer.onClick.AddListener(JoinMultiplayer);

        if (settingsButton != null)
            settingsButton.onClick.AddListener(() => LoadScene(settingsSceneName));

        if (creditsButton != null)
            creditsButton.onClick.AddListener(() => LoadScene(creditsSceneName));

        if (quitButton != null)
            quitButton.onClick.AddListener(QuitGame);

        // Listen for WorldSelectPanel's back event to re-show main menu
        if (_worldSelectPanel != null)
            _worldSelectPanel.OnBackRequested += OnWorldSelectBack;
    }

    /// <summary>
    /// Opens the World Select panel so the player can pick or create a world before launching.
    /// Wired to the "Start Solo" button.
    /// </summary>
    public void StartGame()
    {
        if (_worldSelectPanel != null)
        {
            _worldSelectPanel.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[MainMenu] WorldSelectPanel reference is not assigned — falling back to direct StartSolo().");
            StartSolo();
        }
    }

    /// <summary>
    /// Called when the WorldSelectPanel fires its back event. Re-enables the main menu visuals.
    /// </summary>
    private void OnWorldSelectBack()
    {
        // WorldSelectPanel already hides itself on back — nothing else needed for now.
    }

    /// <summary>
    /// Starts the game as a Solo/Host.
    /// Kept public so other systems (e.g., WorldSelectPanel flow) can still call it directly.
    /// </summary>
    public void StartSolo()
    {
        GameSessionManager.AutoStartNetwork = true;
        GameSessionManager.IsHost = true;
        LoadScene(gameSceneName);
    }

    [Header("Network Input")]
    public TMPro.TMP_InputField ipInputField;
    public TMPro.TMP_InputField portInputField;

    /// <summary>
    /// Starts the game as a Multiplayer Client
    /// </summary>
    public void JoinMultiplayer()
    {
        GameSessionManager.AutoStartNetwork = true;
        GameSessionManager.IsHost = false;

        if (ipInputField != null && !string.IsNullOrEmpty(ipInputField.text))
        {
            GameSessionManager.TargetIP = ipInputField.text.Trim();
        }
        else
        {
            GameSessionManager.TargetIP = "anbuwpr8ly.localto.net";
        }

        if (portInputField != null && ushort.TryParse(portInputField.text.Trim(), out ushort port))
        {
            GameSessionManager.TargetPort = port;
        }
        else
        {
            GameSessionManager.TargetPort = 6547;
        }

        LoadScene(gameSceneName);
    }

    /// <summary>
    /// Loads a scene by name.
    /// </summary>
    /// <param name="sceneName">The name of the scene to load.</param>
    public void LoadScene(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            Debug.Log($"Loading scene: {sceneName}");
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("Invalid scene name!");
        }
    }

    /// <summary>
    /// Loads a scene by its build index.
    /// </summary>
    /// <param name="sceneIndex">The build index of the scene to load.</param>
    public void LoadSceneByIndex(int sceneIndex)
    {
        if (sceneIndex >= 0 && sceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            Debug.Log($"Loading scene index: {sceneIndex}");
            SceneManager.LoadScene(sceneIndex);
        }
        else
        {
            Debug.LogError("Invalid scene index!");
        }
    }

    /// <summary>
    /// Loads a scene asynchronously.
    /// </summary>
    /// <param name="sceneName">The name of the scene to load.</param>
    public void LoadSceneAsync(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            Debug.Log($"Loading scene asynchronously: {sceneName}");
            StartCoroutine(LoadSceneAsyncCoroutine(sceneName));
        }
        else
        {
            Debug.LogError("Invalid scene name!");
        }
    }

    private System.Collections.IEnumerator LoadSceneAsyncCoroutine(string sceneName)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // Optional: prevent automatic scene activation
        // asyncLoad.allowSceneActivation = false;

        while (!asyncLoad.isDone)
        {
            // Progress bar can be displayed here
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            Debug.Log($"Loading progress: {progress * 100}%");

            yield return null;
        }
    }

    /// <summary>
    /// Quits the application.
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("Closing the game");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }

    /// <summary>
    /// Returns to the main menu from another scene.
    /// </summary>
    public void ReturnToMainMenu()
    {
        LoadScene("MainMenu");
    }

    /// <summary>
    /// Restarts the current scene.
    /// </summary>
    public void RestartCurrentScene()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        LoadScene(currentScene);
    }

    private void OnDestroy()
    {
        if (_worldSelectPanel != null)
            _worldSelectPanel.OnBackRequested -= OnWorldSelectBack;
    }
}