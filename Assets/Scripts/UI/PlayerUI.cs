using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUI : MonoBehaviour
{
    [SerializeField] private GameObject character;

    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI playerName;
    [SerializeField] private Button _buttonEquipmentUI;
    [SerializeField] private MWI.UI.TimeUI _timeUI;

    // Le seul lien nécessaire pour la barre d'action
    [SerializeField] private UI_Action_ProgressBar _actionProgressBar;

    [Header("UI Windows Prefabs")]
    [SerializeField] private GameObject _equipmentUIPrefab;

    private GameObject _equipmentUIInstance;
    private Character characterComponent;

    public void Initialize(GameObject newCharacter)
    {
        // On nettoie les anciens liens (Equipement, etc.)
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

        // Lien avec le système de temps
        if (_timeUI != null)
        {
            _timeUI.SetTargetCharacter(characterComponent);
        }

        // On délègue toute la gestion de la barre au script spécialisé
        if (_actionProgressBar != null)
        {
            _actionProgressBar.InitializeCharacterActions(characterComponent.CharacterActions);
        }

        if (_buttonEquipmentUI != null)
        {
            _buttonEquipmentUI.onClick.RemoveAllListeners();
            _buttonEquipmentUI.onClick.AddListener(ToggleEquipmentUI);
        }
    }

    public void ToggleEquipmentUI()
    {
        if (_equipmentUIInstance == null && _equipmentUIPrefab != null)
        {
            _equipmentUIInstance = Instantiate(_equipmentUIPrefab, this.transform);
            var equipmentUIScript = _equipmentUIInstance.GetComponent<CharacterEquipmentUI>();
            if (equipmentUIScript != null)
            {
                equipmentUIScript.Initialize(characterComponent);
            }
            _equipmentUIInstance.SetActive(true);
            return;
        }

        if (_equipmentUIInstance != null)
        {
            _equipmentUIInstance.SetActive(!_equipmentUIInstance.activeSelf);
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
        // Plus besoin de désabonner les actions ici, 
        // c'est UI_Action_ProgressBar qui s'en occupe !
    }

    private void OnDestroy()
    {
        CleanupEvents();
    }
}
