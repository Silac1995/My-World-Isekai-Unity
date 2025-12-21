using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [Header("Boutons du Menu")]
    public Button playButton;
    public Button settingsButton;
    public Button creditsButton;
    public Button quitButton;

    [Header("Noms des Scènes")]
    public string gameSceneName = "GameScene";
    public string settingsSceneName = "SettingsScene";
    public string creditsSceneName = "CreditsScene";

    void Start()
    {
        // Assigner les méthodes aux boutons si ils sont définis
        if (playButton != null)
            playButton.onClick.AddListener(() => LoadScene(gameSceneName));

        if (settingsButton != null)
            settingsButton.onClick.AddListener(() => LoadScene(settingsSceneName));

        if (creditsButton != null)
            creditsButton.onClick.AddListener(() => LoadScene(creditsSceneName));

        if (quitButton != null)
            quitButton.onClick.AddListener(QuitGame);
    }

    /// <summary>
    /// Charge une scène par son nom
    /// </summary>
    /// <param name="sceneName">Le nom de la scène à charger</param>
    public void LoadScene(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            Debug.Log($"Chargement de la scène: {sceneName}");
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError("Nom de scène invalide!");
        }
    }

    /// <summary>
    /// Charge une scène par son index
    /// </summary>
    /// <param name="sceneIndex">L'index de la scène à charger</param>
    public void LoadSceneByIndex(int sceneIndex)
    {
        if (sceneIndex >= 0 && sceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            Debug.Log($"Chargement de la scène index: {sceneIndex}");
            SceneManager.LoadScene(sceneIndex);
        }
        else
        {
            Debug.LogError("Index de scène invalide!");
        }
    }

    /// <summary>
    /// Charge une scène de manière asynchrone
    /// </summary>
    /// <param name="sceneName">Le nom de la scène à charger</param>
    public void LoadSceneAsync(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            Debug.Log($"Chargement asynchrone de la scène: {sceneName}");
            StartCoroutine(LoadSceneAsyncCoroutine(sceneName));
        }
        else
        {
            Debug.LogError("Nom de scène invalide!");
        }
    }

    private System.Collections.IEnumerator LoadSceneAsyncCoroutine(string sceneName)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // Optionnel: empêcher l'activation automatique de la scène
        // asyncLoad.allowSceneActivation = false;

        while (!asyncLoad.isDone)
        {
            // Ici tu peux afficher une barre de progression
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            Debug.Log($"Progression du chargement: {progress * 100}%");

            yield return null;
        }
    }

    /// <summary>
    /// Quitte l'application
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("Fermeture du jeu");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }

    /// <summary>
    /// Retourne au menu principal depuis une autre scène
    /// </summary>
    public void ReturnToMainMenu()
    {
        LoadScene("MainMenu");
    }

    /// <summary>
    /// Redémarre la scène actuelle
    /// </summary>
    public void RestartCurrentScene()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        LoadScene(currentScene);
    }
}