using UnityEngine;
using UnityEngine.UI;


public class UI_InteractionCharacterScript : UI_InteractionScript
{
    [Header("Character Actions")]
    [SerializeField] private Button talkButton;
    [SerializeField] private TMPro.TextMeshProUGUI targetNameText;

    [SerializeField] private Character targetCharacter;

    public void Initialize(Character initiator, Character target)
    {
        base.Initialize(initiator);
        this.targetCharacter = target;

        if (targetNameText != null)
            targetNameText.text = target.CharacterName;

        if (talkButton != null)
            talkButton.onClick.AddListener(OnTalkClicked);
    }

    private void OnTalkClicked()
    {
        Debug.Log($"Dialogue entre {character.CharacterName} et {targetCharacter.CharacterName}");
        Close();
    }
}