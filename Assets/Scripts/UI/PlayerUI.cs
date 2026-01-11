using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUI : MonoBehaviour
{
    [SerializeField] private GameObject character;

    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI playerName;
    [SerializeField] private Button _buttonEquipmentUI; // Vérifie que ce nom est bien écrit ici !

    [Header("UI Windows Prefabs")]
    [SerializeField] private GameObject _equipmentUIPrefab;

    private GameObject _equipmentUIInstance;
    private Character characterComponent;

    // Cette méthode est celle que ton erreur CS0103 ne trouvait pas si elle était mal nommée
    public void Initialize(GameObject newCharacter)
    {
        CleanupEvents();

        if (newCharacter == null)
        {
            ClearUI();
            return;
        }

        this.character = newCharacter;
        this.characterComponent = newCharacter.GetComponent<Character>();

        if (characterComponent == null)
        {
            Debug.LogError("GameObject doesn't have a Character component!");
            ClearUI();
            return;
        }

        UpdatePlayerName();

        // On lie le bouton au clic ici, après l'initialisation
        if (_buttonEquipmentUI != null)
        {
            _buttonEquipmentUI.onClick.RemoveAllListeners(); // Sécurité pour éviter les doubles clics
            _buttonEquipmentUI.onClick.AddListener(ToggleEquipmentUI);
        }
    }

    public void ToggleEquipmentUI()
    {
        // 1. Si l'instance n'existe pas, on la crée
        if (_equipmentUIInstance == null && _equipmentUIPrefab != null)
        {
            _equipmentUIInstance = Instantiate(_equipmentUIPrefab, this.transform);

            var equipmentUIScript = _equipmentUIInstance.GetComponent<CharacterEquipmentUI>();
            if (equipmentUIScript != null)
            {
                equipmentUIScript.Initialize(characterComponent);
            }

            // FORCE l'affichage au premier clic après l'instanciation
            _equipmentUIInstance.SetActive(true);
            return; // On sort pour éviter que la ligne suivante ne le désactive
        }

        // 2. Si l'instance existe déjà, on inverse simplement son état
        if (_equipmentUIInstance != null)
        {
            bool nextState = !_equipmentUIInstance.activeSelf;
            _equipmentUIInstance.SetActive(nextState);
        }
    }

    private void UpdatePlayerName()
    {
        if (playerName != null && characterComponent != null)
            playerName.text = characterComponent.CharacterName;
    }

    private void ClearUI()
    {
        this.character = null;
        this.characterComponent = null;
        if (playerName != null) playerName.text = "";
    }

    private void CleanupEvents()
    {
        // Nettoyage futur des events (HP, etc.)
    }

    private void OnDestroy()
    {
        CleanupEvents();
    }
}