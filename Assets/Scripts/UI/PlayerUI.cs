using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerUI : MonoBehaviour
{
    [SerializeField] private GameObject character; // Privé mais visible dans l'inspecteur

    [Header("UI Components")]
    [SerializeField] private TextMeshProUGUI playerName;

    private Character characterComponent;

    public void Initialize(GameObject newCharacter)
    {
        // Nettoyage des anciens événements si un personnage était déjà assigné
        CleanupEvents();

        if (newCharacter == null)
        {
            Debug.LogWarning("PlayerUI initialized with null character - clearing UI");
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

        // Mise à jour du nom du joueur dans l'UI
        UpdatePlayerName();

        // Ici, tu peux t'abonner aux events du Character (HPChanged, ManaChanged, etc.)
        // Exemple :
        // characterComponent.OnHealthChanged += UpdateHealthBar;
    }

    private void UpdatePlayerName()
    {
        if (playerName != null && characterComponent != null)
        {
            playerName.text = characterComponent.CharacterName;
        }
    }

    // Méthode pour vider l'UI quand aucun personnage n'est sélectionné
    private void ClearUI()
    {
        this.character = null;
        this.characterComponent = null;

        if (playerName != null)
        {
            playerName.text = "";
        }

    }


    // Nettoyage des événements
    private void CleanupEvents()
    {
        if (characterComponent != null)
        {
            // Désabonnement des événements
            // characterComponent.OnHealthChanged -= UpdateHealthBar;
        }
    }

    private void OnDestroy()
    {
        CleanupEvents();
    }
}