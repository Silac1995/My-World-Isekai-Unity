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

        public void Setup(BuildingSO blueprint, Action<string> onSelected)
        {
            if (blueprint == null) return;
            _prefabId = blueprint.PrefabId;
            _onSelected = onSelected;

            if (_icon != null) _icon.sprite = blueprint.Icon;
            if (_nameText != null)
            {
                _nameText.text = !string.IsNullOrEmpty(blueprint.BuildingName)
                    ? blueprint.BuildingName
                    : blueprint.PrefabId;
            }

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
