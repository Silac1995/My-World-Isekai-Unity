using TMPro;
using UnityEngine;

public class UI_StatSlot : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _statNameText;
    [SerializeField] private TextMeshProUGUI _statValueText;

    public void Setup(string statName, string statValue)
    {
        if (_statNameText != null)
        {
            _statNameText.text = statName;
        }

        if (_statValueText != null)
        {
            _statValueText.text = statValue;
        }
    }
}
