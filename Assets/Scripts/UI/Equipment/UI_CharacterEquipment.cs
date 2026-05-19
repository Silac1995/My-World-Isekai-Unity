using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MWI.UI.Equipment
{
    /// <summary>
    /// Root for the equipment window. Variant of UI_WindowBase.prefab (rule #39).
    /// Hosts: 3 special-slot cards (top row), 15 worn mini-cells (5 stacks × 3 layers),
    /// N bag-inventory cells (right grid), 1 shared action popup.
    ///
    /// <para>Subscribes to <see cref="CharacterEquipment.OnEquipmentChanged"/> for
    /// slot updates, <see cref="HandsController.OnCarriedItemChanged"/> for hand-carry
    /// updates (event added in plan-phase Task 1), and <see cref="Inventory.OnInventoryChanged"/>
    /// for bag content updates. No polling.</para>
    ///
    /// <para>Verb dispatch routes through <c>CharacterActions.RequestEquipmentVerbServerRpc</c>
    /// so client-side clicks reach the server-authoritative mutation path (the
    /// direct <c>ExecuteAction</c> call from a client owner runs locally only and
    /// the new actions early-exit on <c>!IsServer</c>).</para>
    /// </summary>
    public sealed class UI_CharacterEquipment : UI_WindowBase
    {
        [Header("Title")]
        [SerializeField] private TextMeshProUGUI _titleLabel;

        [Header("Special slot cards (top row)")]
        [SerializeField] private UI_EquipmentSpecialSlotCard _weaponCard;
        [SerializeField] private UI_EquipmentSpecialSlotCard _handsCard;
        [SerializeField] private UI_EquipmentSpecialSlotCard _bagCard;

        [Header("Paper-doll worn cells")]
        [Tooltip("Authored under the doll stage RectTransform. Up to 15: 5 body slots × 3 layers (U / C / A). Each cell has its own (layer, slot) SerializeFields.")]
        [SerializeField] private List<UI_EquipmentWornCell> _wornCells = new List<UI_EquipmentWornCell>();

        [Header("Bag grid")]
        [SerializeField] private RectTransform _bagCellContainer;
        [SerializeField] private UI_EquipmentBagCell _bagCellPrefab;

        [Header("Popup")]
        [SerializeField] private UI_EquipmentActionPopup _popup;

        private Character _character;
        private readonly List<UI_EquipmentBagCell> _bagCells = new List<UI_EquipmentBagCell>();

        protected override void Awake()
        {
            base.Awake();
            if (_popup != null) _popup.Hide();
        }

        public void Initialize(Character target)
        {
            if (target == null) return;

            UnbindCharacter();

            _character = target;
            if (_titleLabel != null) _titleLabel.text = $"Equipment — {target.CharacterName}";

            BindCharacter();
            BuildBagCells();
            InitializeChildren();
            RepaintAll();
            // NOTE: deliberately does NOT call OpenWindow. Visibility is user-driven
            // (HUD button click → PlayerUI.ToggleEquipmentWindow). Calling OpenWindow
            // here would auto-show the panel as soon as PlayerUI's character-setup pass
            // runs (PlayerUI.cs around line 184). Use InitializeAndOpen for the user
            // intent "open the window now with this character".
        }

        /// <summary>
        /// Binds the window to a target Character AND opens it. Used by
        /// <c>PlayerUI.OpenEquipmentWindow</c> as the canonical "user clicked open" entry.
        /// </summary>
        public void InitializeAndOpen(Character target)
        {
            Initialize(target);
            OpenWindow();
        }

        public override void CloseWindow()
        {
            if (_popup != null) _popup.Hide();
            UnbindCharacter();
            base.CloseWindow();
        }

        private void BindCharacter()
        {
            if (_character == null) return;
            var equip = _character.CharacterEquipment;
            if (equip != null)
            {
                equip.OnEquipmentChanged += OnEquipmentChanged;
                var inv = equip.GetInventory();
                if (inv != null) inv.OnInventoryChanged += OnInventoryChanged;
            }
            var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null) hands.OnCarriedItemChanged += OnHandsCarryChanged;
        }

        private void UnbindCharacter()
        {
            if (_character == null) return;
            var equip = _character.CharacterEquipment;
            if (equip != null)
            {
                equip.OnEquipmentChanged -= OnEquipmentChanged;
                var inv = equip.GetInventory();
                if (inv != null) inv.OnInventoryChanged -= OnInventoryChanged;
            }
            var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null) hands.OnCarriedItemChanged -= OnHandsCarryChanged;
            _character = null;
        }

        private void InitializeChildren()
        {
            _weaponCard?.Initialize(this);
            _handsCard?.Initialize(this);
            _bagCard?.Initialize(this);
            for (int i = 0; i < _wornCells.Count; i++)
            {
                if (_wornCells[i] != null) _wornCells[i].Initialize(this);
            }
        }

        private void BuildBagCells()
        {
            for (int i = 0; i < _bagCells.Count; i++)
            {
                if (_bagCells[i] != null) Destroy(_bagCells[i].gameObject);
            }
            _bagCells.Clear();

            if (_character == null) return;
            var inv = _character.CharacterEquipment?.GetInventory();
            if (inv == null || _bagCellContainer == null || _bagCellPrefab == null) return;

            for (int i = 0; i < inv.ItemSlots.Count; i++)
            {
                var cell = Instantiate(_bagCellPrefab, _bagCellContainer);
                cell.gameObject.SetActive(true);
                bool isWeapon = inv.ItemSlots[i] is WeaponSlot;
                cell.Initialize(this, i, isWeapon);
                _bagCells.Add(cell);
            }
        }

        private void RepaintAll()
        {
            if (_character == null) return;
            var equip = _character.CharacterEquipment;
            if (equip == null) return;

            _weaponCard?.RefreshActiveWeapon(equip.CurrentWeapon);

            var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
            _handsCard?.RefreshHandsCarry(hands?.CarriedItem);

            var bag = equip.GetBagInstance();
            var inv = equip.GetInventory();
            int used = 0;
            int cap = inv != null ? inv.Capacity : 0;
            if (inv != null)
            {
                for (int i = 0; i < inv.ItemSlots.Count; i++)
                    if (!inv.ItemSlots[i].IsEmpty()) used++;
            }
            _bagCard?.RefreshEquippedBag(bag, used, cap);

            for (int i = 0; i < _wornCells.Count; i++)
            {
                var cell = _wornCells[i];
                if (cell == null) continue;
                EquipmentLayer layer = ResolveLayer(equip, cell.Layer);
                EquipmentInstance inst = layer != null ? layer.GetInstance(cell.Slot) : null;
                cell.Refresh(inst);
            }

            if (inv != null)
            {
                for (int i = 0; i < _bagCells.Count && i < inv.ItemSlots.Count; i++)
                {
                    var slot = inv.ItemSlots[i];
                    _bagCells[i].Refresh(slot.IsEmpty() ? null : slot.ItemInstance);
                }
            }
        }

        // -------------------------------------------------------------
        // Event callbacks — all just route to RepaintAll.
        // -------------------------------------------------------------
        private void OnEquipmentChanged() => RepaintAll();
        private void OnInventoryChanged() => RepaintAll();
        private void OnHandsCarryChanged(ItemInstance _) => RepaintAll();

        // -------------------------------------------------------------
        // Popup entry points called by leaf cells.
        // -------------------------------------------------------------
        public void OpenPopupForBagCell(UI_EquipmentBagCell cell)
        {
            if (cell == null || _character == null || _popup == null) return;
            var inv = _character.CharacterEquipment?.GetInventory();
            if (inv == null || cell.SlotIndex < 0 || cell.SlotIndex >= inv.ItemSlots.Count) return;
            var slot = inv.ItemSlots[cell.SlotIndex];
            if (slot.IsEmpty()) return;
            ItemInstance item = slot.ItemInstance;

            var verbs = BuildBagVerbs(item);
            EquipmentSourceRef source = EquipmentSourceRef.Bag(cell.SlotIndex);
            _popup.Show(
                (RectTransform)cell.transform,
                item.ItemSO.ItemName,
                BuildBagSubtitle(item),
                verbs,
                v => OnVerbSelected(v, source));
        }

        public void OpenPopupForWornCell(UI_EquipmentWornCell cell)
        {
            if (cell == null || _character == null || _popup == null) return;
            var equip = _character.CharacterEquipment;
            if (equip == null) return;
            EquipmentLayer layer = ResolveLayer(equip, cell.Layer);
            EquipmentInstance inst = layer?.GetInstance(cell.Slot);
            if (inst == null) return;

            var verbs = new List<EquipmentVerb>
            {
                new EquipmentVerb(EquipmentVerbId.Unequip,      "Unequip"),
                new EquipmentVerb(EquipmentVerbId.CarryInHand,  "Carry in hand"),
                new EquipmentVerb(EquipmentVerbId.DropToGround, "Drop on ground", isDanger: true),
            };
            EquipmentSourceRef source = EquipmentSourceRef.Worn(cell.Layer, cell.Slot);
            _popup.Show(
                (RectTransform)cell.transform,
                inst.ItemSO.ItemName,
                $"{cell.Layer} layer · {cell.Slot}",
                verbs,
                v => OnVerbSelected(v, source));
        }

        public void OpenPopupForSpecialCard(UI_EquipmentSpecialSlotCard card)
        {
            if (card == null || _character == null || _popup == null) return;
            var equip = _character.CharacterEquipment;
            if (equip == null) return;

            switch (card.Kind)
            {
                case UI_EquipmentSpecialSlotCard.SlotKind.ActiveWeapon:
                {
                    WeaponInstance w = equip.CurrentWeapon;
                    if (w == null) return;
                    var verbs = new List<EquipmentVerb>
                    {
                        new EquipmentVerb(EquipmentVerbId.StashInBag,   "Stash in bag"),
                        new EquipmentVerb(EquipmentVerbId.CarryInHand,  "Carry in hand"),
                        new EquipmentVerb(EquipmentVerbId.DropToGround, "Drop on ground", isDanger: true),
                    };
                    _popup.Show((RectTransform)card.transform, w.ItemSO.ItemName, "Weapon · currently wielded", verbs,
                        v => OnVerbSelected(v, EquipmentSourceRef.Weapon()));
                    break;
                }
                case UI_EquipmentSpecialSlotCard.SlotKind.HandsCarry:
                {
                    var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
                    if (hands == null || !hands.IsCarrying) return;
                    ItemInstance c = hands.CarriedItem;
                    var verbs = new List<EquipmentVerb>
                    {
                        new EquipmentVerb(EquipmentVerbId.StashInBag, "Stash in bag"),
                    };
                    if (c is ConsumableInstance)
                        verbs.Add(new EquipmentVerb(EquipmentVerbId.UseConsumable, "Use"));
                    verbs.Add(new EquipmentVerb(EquipmentVerbId.DropToGround, "Drop on ground", isDanger: true));
                    _popup.Show((RectTransform)card.transform, c.ItemSO.ItemName, "Carried in hand", verbs,
                        v => OnVerbSelected(v, EquipmentSourceRef.Hands()));
                    break;
                }
                case UI_EquipmentSpecialSlotCard.SlotKind.EquippedBag:
                {
                    BagInstance b = equip.GetBagInstance();
                    if (b == null) return;
                    var verbs = new List<EquipmentVerb>
                    {
                        new EquipmentVerb(EquipmentVerbId.UnequipBag, "Unequip bag", isDanger: true),
                    };
                    // EquippedBag source kind is irrelevant for UnequipBag (RPC routes by verbId
                    // directly to UnequipBag() server-side; payload is ignored). Pass Hands() as
                    // a harmless placeholder.
                    _popup.Show((RectTransform)card.transform, b.ItemSO.ItemName, "Equipped bag (drops with contents)", verbs,
                        v => OnVerbSelected(v, EquipmentSourceRef.Hands()));
                    break;
                }
            }
        }

        // -------------------------------------------------------------
        // Verb dispatch — routes through the server RPC bridge.
        // -------------------------------------------------------------
        private void OnVerbSelected(EquipmentVerb verb, EquipmentSourceRef source)
        {
            if (_character == null) return;
            var actions = _character.CharacterActions;
            if (actions == null) return;

            byte verbId = (byte)verb.Id;
            byte sourceKind = (byte)source.Kind;
            int bagIndex = source.BagIndex;
            int layer = (int)source.Layer;
            int slot = (int)source.Slot;

            actions.RequestEquipmentVerbServerRpc(verbId, sourceKind, bagIndex, layer, slot);
        }

        // -------------------------------------------------------------
        // Helpers.
        // -------------------------------------------------------------
        private static List<EquipmentVerb> BuildBagVerbs(ItemInstance item)
        {
            var verbs = new List<EquipmentVerb>();
            if (item is WearableInstance)
                verbs.Add(new EquipmentVerb(EquipmentVerbId.Equip, "Equip"));
            else if (item is ConsumableInstance)
                verbs.Add(new EquipmentVerb(EquipmentVerbId.UseConsumable, "Use"));
            // Weapons fall through to plain Carry/Drop — active-swap lives on the combat HUD.
            verbs.Add(new EquipmentVerb(EquipmentVerbId.CarryInHand, "Carry in hand"));
            verbs.Add(new EquipmentVerb(EquipmentVerbId.DropToGround, "Drop on ground", isDanger: true));
            return verbs;
        }

        private static string BuildBagSubtitle(ItemInstance item)
        {
            if (item is WearableInstance && item.ItemSO is WearableSO ws)
                return $"Wearable · {ws.EquipmentLayer} · {ws.WearableType}";
            if (item is ConsumableInstance) return "Consumable";
            if (item is WeaponInstance) return "Weapon";
            return item.ItemSO?.GetType().Name ?? "Item";
        }

        private static EquipmentLayer ResolveLayer(CharacterEquipment equip, WearableLayerEnum layer) => layer switch
        {
            WearableLayerEnum.Underwear => equip.UnderwearLayer,
            WearableLayerEnum.Clothing  => equip.ClothingLayer,
            WearableLayerEnum.Armor     => equip.ArmorLayer,
            _ => null,
        };

        private void OnDestroy()
        {
            UnbindCharacter();
        }
    }
}
