using UnityEngine;
using UnityEngine.UI;

public class UI_InteractionItem : UI_InteractionScript
{
    [Header("Item Actions")]
    [SerializeField] private Button pickUpButton;
    [SerializeField] private TMPro.TextMeshProUGUI itemNameText;

    [SerializeField] private WorldItem targetItem;

    public void Initialize(Character initiator, WorldItem item)
    {
        base.Initialize(initiator);
        this.targetItem = item;

        if (itemNameText != null && item.ItemInstance != null)
            itemNameText.text = item.ItemInstance.ItemSO.ItemName;

        if (pickUpButton != null)
            pickUpButton.onClick.AddListener(OnPickUpClicked);
    }

    private void OnPickUpClicked()
    {
        Debug.Log($"{characterInitiator.CharacterName} ramasse {targetItem.name}");
        // Logique réseau future : characterInitiator.RequestPickUp(targetItem);
        Close();
    }
}