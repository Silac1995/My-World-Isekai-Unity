using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generic slot-grid renderer used for both halves of <see cref="UI_StorageFurniturePanel"/>:
/// the player's bag inventory on the left and the chest's slots on the right.
///
/// Pool-based: keeps one <see cref="Button"/> per <see cref="_slotPrefab"/> instance and
/// reuses them across rebinds to avoid per-rebind allocations. Empty slots render as
/// non-interactable buttons labelled "(empty)".
///
/// Left-click delivers the slot's <see cref="ItemInstance"/> to the consumer-supplied
/// <see cref="_clickCallback"/>. The grid does not own the action — the panel does — so
/// this script stays UI-pure.
/// </summary>
public class UI_StorageGrid : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Prefab used for each slot button. Must have Button + child TMP_Text + child Image (icon).")]
    [SerializeField] private GameObject _slotPrefab;

    [Tooltip("Layout root that holds the instantiated slot buttons (typically a GridLayoutGroup).")]
    [SerializeField] private Transform _slotContainer;

    [Tooltip("Optional capacity label like '3 / 8'. May be null.")]
    [SerializeField] private TextMeshProUGUI _capacityLabel;

    private readonly List<SlotInstance> _pool = new List<SlotInstance>();
    private Action<ItemInstance> _clickCallback;
    private Func<bool> _interactableGate;

    private class SlotInstance
    {
        public GameObject Root;
        public Button Button;
        public TextMeshProUGUI Label;
        public Image Icon;
        public bool IsPopulated;   // set in Bind, read in RefreshInteractable
    }

    /// <summary>
    /// Render <paramref name="slots"/>. <paramref name="onSlotLeftClicked"/> is invoked
    /// with the slot's <see cref="ItemInstance"/> on left-click of a populated slot.
    /// <paramref name="interactableGate"/> is queried per-frame in <see cref="RefreshInteractable"/>
    /// to gray out clicks while an action is in flight; pass null to skip the gate.
    /// </summary>
    public void Bind(IReadOnlyList<ItemSlot> slots, Action<ItemInstance> onSlotLeftClicked, Func<bool> interactableGate)
    {
        _clickCallback = onSlotLeftClicked;
        _interactableGate = interactableGate;

        // Hide all pooled slots first
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Root != null) _pool[i].Root.SetActive(false);
        }

        if (slots == null)
        {
            UpdateCapacityLabel(0, 0);
            return;
        }

        int occupied = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            SlotInstance inst = i < _pool.Count ? _pool[i] : Grow();
            if (inst == null) continue;   // Grow logged the misconfiguration; skip safely.
            inst.Root.SetActive(true);

            ItemSlot slot = slots[i];
            ItemInstance item = slot != null ? slot.ItemInstance : null;
            bool empty = item == null;

            if (inst.Label != null)
            {
                inst.Label.text = empty ? "<color=#666666>(empty)</color>" : item.CustomizedName;
            }
            if (inst.Icon != null)
            {
                if (!empty && item.ItemSO != null && item.ItemSO.Icon != null)
                {
                    inst.Icon.sprite = item.ItemSO.Icon;
                    inst.Icon.enabled = true;
                }
                else
                {
                    inst.Icon.enabled = false;
                }
            }

            inst.Button.onClick.RemoveAllListeners();
            ItemInstance capturedItem = item;   // snapshot for closure — avoids list mutation race
            inst.Button.onClick.AddListener(() =>
            {
                if (capturedItem != null) _clickCallback?.Invoke(capturedItem);
            });
            inst.Button.interactable = !empty;

            inst.IsPopulated = !empty;

            if (!empty) occupied++;
        }

        UpdateCapacityLabel(occupied, slots.Count);
    }

    /// <summary>
    /// Re-evaluate the interactable gate (typically "is the character idle?") on every
    /// pooled, populated slot. Call from the panel's Update() so a deposit-in-progress
    /// grays out further clicks until the action finishes.
    /// </summary>
    public void RefreshInteractable()
    {
        bool gate = _interactableGate == null || _interactableGate.Invoke();
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Root == null || !_pool[i].Root.activeSelf) continue;
            // Empty slots stay non-interactable (set in Bind). Populated slots track the gate.
            _pool[i].Button.interactable = _pool[i].IsPopulated && gate;
        }
    }

    private SlotInstance Grow()
    {
        if (_slotPrefab == null || _slotContainer == null)
        {
            Debug.LogError($"<color=red>[UI_StorageGrid]</color> {name}: _slotPrefab or _slotContainer not assigned.");
            return null;
        }

        GameObject go = Instantiate(_slotPrefab, _slotContainer);
        SlotInstance inst = new SlotInstance
        {
            Root = go,
            Button = go.GetComponent<Button>(),
            Label = go.GetComponentInChildren<TextMeshProUGUI>(true),
            Icon = go.GetComponentInChildren<Image>(true),
        };
        _pool.Add(inst);
        return inst;
    }

    private void UpdateCapacityLabel(int occupied, int total)
    {
        if (_capacityLabel == null) return;
        _capacityLabel.text = $"{occupied} / {total}";
    }

    private void OnDestroy()
    {
        Unbind();
    }

    /// <summary>Clears the bound callback. Call before destroying the panel.</summary>
    public void Unbind()
    {
        _clickCallback = null;
        _interactableGate = null;
        for (int i = 0; i < _pool.Count; i++)
        {
            if (_pool[i].Button != null) _pool[i].Button.onClick.RemoveAllListeners();
            if (_pool[i].Root != null) _pool[i].Root.SetActive(false);
        }
    }
}
