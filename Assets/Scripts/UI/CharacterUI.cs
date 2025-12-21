using TMPro;
using UnityEngine;

public class CharacterUI : MonoBehaviour
{
    [SerializeField] private Vector3 offset = new Vector3(0, 2f, 0); // Position au-dessus du perso

    [SerializeField] private Character character;
    public Character Character => character;

    [Header("UI Elements")]
    [SerializeField] private StatBar healthBar;
    public StatBar HealthBar => healthBar;

    [SerializeField] private StatBar staminaBar;
    public StatBar StaminaBar => staminaBar;

    [SerializeField] private StatBar manaBar;
    public StatBar ManaBar => manaBar;

    [SerializeField] private StatBar initiativeBar;
    public StatBar InitiativeBar => initiativeBar;

    [Header("Character Name")]
    [SerializeField] private TextMeshProUGUI nameText; // Assigné dans l’Inspector

    private string lastCharacterName;

    private void Awake()
    {
        UpdateNameText();
    }

    private void LateUpdate()
    {
        if (character == null || Camera.main == null)
            return;

        // Position UI
        transform.position = character.transform.position + offset;

        // Orientation vers caméra (axe Y fixe)
        Vector3 directionToCamera = Camera.main.transform.position - transform.position;
        directionToCamera.y = 0f;
        if (directionToCamera != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(-directionToCamera, Vector3.up);
        }

        // Mise à jour du nom si changement
        if (character.CharacterName != lastCharacterName)
        {
            UpdateNameText();
        }
    }


    private void UpdateNameText()
    {
        if (nameText != null)
        {
            lastCharacterName = character.CharacterName;
            nameText.text = lastCharacterName;
        }
    }
}
