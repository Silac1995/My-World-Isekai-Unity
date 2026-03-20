using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_StatSlot : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _statNameText;
    [SerializeField] private TextMeshProUGUI _statValueText;
    [SerializeField] private Button _upgradeButton;

    public void Setup(string statName, string statValue, bool canUpgrade = false, Action onUpgradeClicked = null)
    {
        if (_statNameText != null)
        {
            _statNameText.text = statName;
        }

        if (_statValueText != null)
        {
            _statValueText.text = statValue;
        }

        if (_upgradeButton != null)
        {
            _upgradeButton.gameObject.SetActive(canUpgrade);
            _upgradeButton.onClick.RemoveAllListeners();
            if (canUpgrade && onUpgradeClicked != null)
            {
                _upgradeButton.onClick.AddListener(() => onUpgradeClicked.Invoke());
            }
        }
    }
}
