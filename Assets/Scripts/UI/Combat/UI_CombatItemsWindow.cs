using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MWI.UI.Combat
{
    /// <summary>
    /// UI_WindowBase variant (CLAUDE.md rule #39). Lists usable consumables. Anchored
    /// above-right of the Items button in the combat action bar. Auto-closes on:
    /// use · combat end · ESC · second-click toggle on Items button · OnDisable.
    /// Hotkeys 1-9 (window-scoped) fire row N — PlayerController suppresses its
    /// global 1-6 ability binding while IsOpen is true.
    /// </summary>
    public class UI_CombatItemsWindow : UI_WindowBase
    {
        [Header("Wiring")]
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private UI_CombatItemRow _rowPrefab;
        [SerializeField] private TMP_Text _headerCountText;

        private Character _customer;
        private readonly List<UI_CombatItemRow> _rows = new List<UI_CombatItemRow>();

        // Hotkey-to-consumable map. Index = key press 1-9 - 1; value = the consumable
        // the row at that index is bound to (only enabled rows occupy slots).
        private readonly List<ConsumableInstance> _hotkeyOrder = new List<ConsumableInstance>();

        public bool IsOpen => gameObject != null && gameObject.activeSelf;

        /// <summary>
        /// Bind the window to a character. Builds the row list from the character's
        /// inventory consumables. Subscribes to OnBattleLeft for auto-close.
        /// Idempotent — safe to call repeatedly (rebuilds rows + re-subscribes).
        /// </summary>
        public void Initialize(Character customer)
        {
            ClearRows();
            UnsubscribeCustomer();

            _customer = customer;
            if (_customer == null) return;

            if (_customer.CharacterCombat != null)
            {
                _customer.CharacterCombat.OnBattleLeft += HandleBattleLeft;
            }

            BuildRows();
        }

        private void BuildRows()
        {
            if (_customer == null) return;

            var inventory = _customer.CharacterEquipment != null
                ? _customer.CharacterEquipment.GetInventory()
                : null;
            if (inventory == null)
            {
                if (_headerCountText != null) _headerCountText.text = "0 available";
                return;
            }

            int hotkeyIdx = 1; // 1-9 visible hotkey badges
            int enabledCount = 0;

            foreach (var consumable in inventory.GetConsumables())
            {
                var row = Instantiate(_rowPrefab, _rowContainer);
                bool usable = consumable.ItemSO is ConsumableSO so && so.IsUsableInCombat;
                int assignedKey = (usable && hotkeyIdx <= 9) ? hotkeyIdx : 0;
                row.Initialize(consumable, assignedKey, OnRowUsed);
                _rows.Add(row);

                if (usable)
                {
                    enabledCount++;
                    if (hotkeyIdx <= 9)
                    {
                        // Track hotkey-to-row mapping for keyboard fire.
                        while (_hotkeyOrder.Count < hotkeyIdx) _hotkeyOrder.Add(null);
                        _hotkeyOrder[hotkeyIdx - 1] = consumable;
                        hotkeyIdx++;
                    }
                }
            }

            if (_headerCountText != null) _headerCountText.text = $"{enabledCount} available";
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
            {
                if (row != null) Destroy(row.gameObject);
            }
            _rows.Clear();
            _hotkeyOrder.Clear();
        }

        private void OnRowUsed(ConsumableInstance instance)
        {
            if (_customer == null || instance == null) { CloseWindow(); return; }

            // For self-target consumables, target = customer.
            // For thrown items the downstream action can use combat.PlannedTarget;
            // TryQueueUseItem currently always routes through CharacterUseConsumableAction
            // which ignores the target. Future polish (spec §12 deferred): extend
            // CharacterUseConsumableAction to accept a target for throw items.
            Character target = _customer.CharacterCombat != null ? _customer.CharacterCombat.PlannedTarget : null;
            _customer.CharacterCombat?.TryQueueUseItem(instance, target);
            CloseWindow();
        }

        private void HandleBattleLeft() => CloseWindow();

        private void Update()
        {
            if (!IsOpen) return;

            // Window-scoped hotkeys 1-9 for row use.
            for (int i = 0; i < _hotkeyOrder.Count && i < 9; i++)
            {
                var key = (KeyCode)(KeyCode.Alpha1 + i);
                if (Input.GetKeyDown(key) && _hotkeyOrder[i] != null)
                {
                    OnRowUsed(_hotkeyOrder[i]);
                    return;
                }
            }

            // ESC closes the window. ESC handling is intentionally local — PlayerController
            // should not own this binding while the window is open. If a future system needs
            // ESC priority management, add an IsCombatItemsWindowOpen gate there.
            if (Input.GetKeyDown(KeyCode.Escape)) CloseWindow();
        }

        public override void CloseWindow()
        {
            UnsubscribeCustomer();
            _customer = null;
            ClearRows();
            base.CloseWindow();
        }

        private void UnsubscribeCustomer()
        {
            if (_customer != null && _customer.CharacterCombat != null)
            {
                _customer.CharacterCombat.OnBattleLeft -= HandleBattleLeft;
            }
        }

        protected override void OnDestroy()
        {
            UnsubscribeCustomer();
            base.OnDestroy();
        }
    }
}
