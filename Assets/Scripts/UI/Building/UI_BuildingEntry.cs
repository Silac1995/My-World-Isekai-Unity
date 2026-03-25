using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MWI.WorldSystem;
using System;

namespace MWI.UI.Building
{
    public class UI_BuildingEntry : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private Button _selectButton;

        private string _prefabId;
        private Action<string> _onSelected;

        public void Setup(BuildingRegistryEntry entry, Action<string> onSelected)
        {
            _prefabId = entry.PrefabId;
            _onSelected = onSelected;

            if (_icon != null) _icon.sprite = entry.Icon;
            if (_nameText != null) _nameText.text = entry.BuildingName;

            if (_selectButton != null)
            {
                _selectButton.onClick.RemoveAllListeners();
                _selectButton.onClick.AddListener(OnClicked);
            }
        }

        private void OnClicked()
        {
            _onSelected?.Invoke(_prefabId);
        }
    }
}
