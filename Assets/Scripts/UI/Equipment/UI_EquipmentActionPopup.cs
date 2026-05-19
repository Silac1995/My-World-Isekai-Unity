using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// State-aware popup for the equipment window. Same component used by every
    /// item-bearing cell (bag, worn mini-cell, special slot cards). Fed a verb list
    /// per state — see <see cref="UI_CharacterEquipment.OpenPopupForBagCell"/>,
    /// <see cref="UI_CharacterEquipment.OpenPopupForWornCell"/>,
    /// <see cref="UI_CharacterEquipment.OpenPopupForSpecialCard"/>.
    ///
    /// <para>Dismissal: ESC, click outside, OR button click (action then close).
    /// One instance per <see cref="UI_CharacterEquipment"/>; activated/deactivated
    /// on each click, not instantiated per click (cheap).</para>
    /// </summary>
    public sealed class UI_EquipmentActionPopup : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private RectTransform _root;
        [SerializeField] private TextMeshProUGUI _titleLabel;
        [SerializeField] private TextMeshProUGUI _subtitleLabel;
        [SerializeField] private RectTransform _buttonContainer;
        [SerializeField] private Button _buttonPrefab;

        private readonly List<Button> _spawnedButtons = new List<Button>();
        private Action<EquipmentVerb> _verbCallback;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            if (_root == null) _root = (RectTransform)transform;
            gameObject.SetActive(false);
        }

        public void Show(
            RectTransform anchor,
            string title,
            string subtitle,
            IReadOnlyList<EquipmentVerb> verbs,
            Action<EquipmentVerb> onVerbSelected)
        {
            if (anchor == null || verbs == null || verbs.Count == 0)
            {
                Hide();
                return;
            }

            if (_titleLabel != null) _titleLabel.text = title ?? string.Empty;
            if (_subtitleLabel != null) _subtitleLabel.text = subtitle ?? string.Empty;
            _verbCallback = onVerbSelected;

            ClearButtons();

            if (_buttonPrefab == null || _buttonContainer == null)
            {
                Debug.LogWarning("<color=orange>[UI_EquipmentActionPopup]</color> _buttonPrefab or _buttonContainer SerializeField is null — author the popup prefab + wire them.");
                return;
            }

            for (int i = 0; i < verbs.Count; i++)
            {
                EquipmentVerb verb = verbs[i];
                Button btn = Instantiate(_buttonPrefab, _buttonContainer);
                btn.gameObject.SetActive(true);
                var lbl = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (lbl != null) lbl.text = verb.Label;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnButtonClicked(verb));
                _spawnedButtons.Add(btn);
            }

            PositionNearAnchor(anchor);
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _verbCallback = null;
            ClearButtons();
        }

        private void ClearButtons()
        {
            for (int i = 0; i < _spawnedButtons.Count; i++)
            {
                if (_spawnedButtons[i] != null) Destroy(_spawnedButtons[i].gameObject);
            }
            _spawnedButtons.Clear();
        }

        private void OnButtonClicked(EquipmentVerb verb)
        {
            var cb = _verbCallback;
            Hide();
            cb?.Invoke(verb);
        }

        private void PositionNearAnchor(RectTransform anchor)
        {
            // Naive placement — anchor.position + small offset to the right.
            // Refinement (clip-to-screen, side-flip) is prefab-authoring polish, not blocking.
            _root.position = anchor.position + (Vector3)new Vector2(anchor.rect.width * 0.5f + 12f, 0f);
        }

        private void Update()
        {
            if (!IsOpen) return;

            // ESC dismisses.
            if (Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }

            // Click-outside-to-dismiss: only when the click lands OUTSIDE any UI element
            // (i.e. on the game world). Uses EventSystem.IsPointerOverGameObject — works in
            // ScreenSpaceCamera mode without needing a camera reference. The earlier
            // RectangleContainsScreenPoint(_root, mouse, null) variant mis-computed under
            // ScreenSpaceCamera and dismissed the popup BEFORE the EventSystem could route
            // the click to a popup button, so buttons were unclickable.
            if (Input.GetMouseButtonDown(0))
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && !es.IsPointerOverGameObject())
                {
                    Hide();
                }
            }
        }

        private void OnDestroy()
        {
            ClearButtons();
        }
    }

    /// <summary>
    /// A single popup entry: label + behavior identifier. The parent window maps
    /// VerbId to a concrete server RPC call.
    /// </summary>
    public readonly struct EquipmentVerb
    {
        public readonly EquipmentVerbId Id;
        public readonly string Label;
        public readonly bool IsDanger;

        public EquipmentVerb(EquipmentVerbId id, string label, bool isDanger = false)
        {
            Id = id; Label = label; IsDanger = isDanger;
        }
    }

    /// <summary>
    /// Equipment-window verb identifiers. Values mirror the byte constants on
    /// <c>CharacterActions</c> (EQUIP_VERB_* in CharacterActions.cs) so the UI
    /// can convert directly when calling RequestEquipmentVerbServerRpc.
    /// </summary>
    public enum EquipmentVerbId : byte
    {
        Equip         = 0,
        Unequip       = 1,
        CarryInHand   = 2,
        StashInBag    = 3,
        UseConsumable = 4,
        UnequipBag    = 5,
        DropToGround  = 6,
    }
}
