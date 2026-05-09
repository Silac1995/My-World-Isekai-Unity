using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD panel that opens when the local owner-player taps E on a <see cref="StorageFurniture"/>.
///
/// Composition:
/// - Left side: hands sub-slot (one button) + bag grid (a <see cref="UI_StorageGrid"/> bound to the
///   character's bag inventory slots).
/// - Right side: chest grid (a second <see cref="UI_StorageGrid"/> bound to the storage furniture's
///   <see cref="StorageFurniture.ItemSlots"/>).
///
/// Click on left = queue <see cref="CharacterStoreInFurnitureAction"/>.
/// Click on right = queue <see cref="CharacterTakeFromFurnitureAction"/>.
/// Both actions are server-authoritative; the panel just constructs and queues them — same as
/// what GOAP does for NPCs (see GoapAction_GatherStorageItems / GoapAction_DepositResources /
/// GoapAction_TakeFromSourceFurniture / GoapAction_StageItemForPickup).
///
/// Closes on: ESC, target despawn, character incapacitated, character entering combat, or the
/// player walking out of the storage's interaction zone (polled in Update).
/// </summary>
public class UI_StorageFurniturePanel : UI_WindowBase
{
    [Header("Wiring (assign in prefab)")]
    [SerializeField] private TextMeshProUGUI _chestNameLabel;

    [Header("Left side — character")]
    [SerializeField] private Button _handsSlotButton;
    [SerializeField] private TextMeshProUGUI _handsSlotLabel;
    [SerializeField] private Image _handsSlotIcon;
    [SerializeField] private UI_StorageGrid _bagGrid;
    [SerializeField] private GameObject _bagGridRoot;
    [SerializeField] private TextMeshProUGUI _noBagLabel;

    [Header("Right side — chest")]
    [SerializeField] private UI_StorageGrid _chestGrid;

    private StorageFurniture _target;
    private Character _interactor;
    private FurnitureInteractable _targetInteractable;
    private ItemInstance _lastHandsItem;

    /// <summary>
    /// Called by <see cref="PlayerUI.OpenStoragePanel"/>. Activates the panel, wires up
    /// subscriptions, and paints the initial state. Calling this while already open
    /// re-binds to the new target cleanly.
    /// </summary>
    public void Initialize(StorageFurniture target, Character interactor)
    {
        if (target == null || interactor == null)
        {
            Debug.LogWarning("<color=orange>[StoragePanel]</color> Initialize called with null target or interactor.");
            return;
        }

        UnsubscribeAll();

        _target = target;
        _interactor = interactor;
        _targetInteractable = target.GetComponent<FurnitureInteractable>();
        _lastHandsItem = null;

        if (_chestNameLabel != null) _chestNameLabel.text = target.FurnitureName;

        // Close button is wired by UI_WindowBase.Awake to call CloseWindow().
        if (_handsSlotButton != null)
        {
            _handsSlotButton.onClick.RemoveAllListeners();
            _handsSlotButton.onClick.AddListener(OnHandsSlotClicked);
        }

        _target.OnInventoryChanged += HandleStorageChanged;

        var equipment = _interactor.CharacterEquipment;
        if (equipment != null)
        {
            equipment.OnEquipmentChanged += HandleEquipmentChanged;
        }

        OpenWindow();

        RepaintAll();
    }

    /// <summary>
    /// Closes the panel: unsubscribes events, unbinds grids, then defers to
    /// <see cref="UI_WindowBase.CloseWindow"/> for the SetActive(false). Called by the
    /// inherited close button (auto-wired in <see cref="UI_WindowBase.Awake"/>), the ESC
    /// handler in <see cref="Update"/>, and external callers like
    /// <see cref="PlayerUI.CloseStoragePanel"/>.
    /// </summary>
    public override void CloseWindow()
    {
        UnsubscribeAll();
        _target = null;
        _interactor = null;
        _targetInteractable = null;
        _lastHandsItem = null;

        if (_chestGrid != null) _chestGrid.Unbind();
        if (_bagGrid != null) _bagGrid.Unbind();

        base.CloseWindow();
    }

    private void UnsubscribeAll()
    {
        // Unity fake-null safety: a destroyed UnityEngine.Object reports `!= null` in C#
        // but throws on member access. Use the Unity `==`-overloaded comparison (which
        // checks the native object liveness) AND a member-access null-conditional `?.`
        // chain so we never deref a destroyed Character/StorageFurniture/CharacterEquipment.

        if (_target != null)
        {
            // _target is a StorageFurniture (Furniture : MonoBehaviour). The `!= null`
            // check above goes through Unity's overloaded operator, so a destroyed
            // GameObject correctly reports null and we skip safely.
            _target.OnInventoryChanged -= HandleStorageChanged;
        }

        // Two separate null checks: the Character and its equipment subsystem. Both are
        // UnityEngine.Object-derived; both need the Unity-aware `!= null` check.
        if (_interactor != null)
        {
            var equipment = _interactor.CharacterEquipment;
            if (equipment != null)
            {
                equipment.OnEquipmentChanged -= HandleEquipmentChanged;
            }
        }
    }

    private void OnDisable() => UnsubscribeAll();

    /// <summary>
    /// Override <see cref="UI_WindowBase.OnDestroy"/> to also tear down our event
    /// subscriptions on top of the base's close-button cleanup.
    /// </summary>
    protected override void OnDestroy()
    {
        UnsubscribeAll();
        base.OnDestroy();
    }

    private void Update()
    {
        if (_target == null || _interactor == null) { CloseWindow(); return; }

        // ESC closes the panel (rule #33 carve-out: input that targets the UI itself stays in the UI).
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseWindow();
            return;
        }

        // Hands have no event — poll each frame, mirror CharacterEquipmentUI.RefreshHandsButton.
        var hands = _interactor.CharacterVisual?.BodyPartsController?.HandsController;
        ItemInstance carried = hands != null ? hands.CarriedItem : null;
        if (carried != _lastHandsItem)
        {
            _lastHandsItem = carried;
            RepaintHandsSlot();
        }

        // Auto-close when player walks out of interaction zone.
        if (_targetInteractable != null && !_targetInteractable.IsCharacterInInteractionZone(_interactor))
        {
            CloseWindow();
            return;
        }

        // Re-evaluate per-slot interactable so the action-busy gate visually grays buttons.
        if (_chestGrid != null) _chestGrid.RefreshInteractable();
        if (_bagGrid != null) _bagGrid.RefreshInteractable();
        RefreshHandsInteractable(carried);
    }

    private void HandleStorageChanged()
    {
        if (_target == null) return;
        _chestGrid?.Bind(_target.ItemSlots, OnChestSlotClicked, IsCharacterIdle);
    }

    private void HandleEquipmentChanged()
    {
        RepaintBagSide();
    }

    private void RepaintAll()
    {
        HandleStorageChanged();
        RepaintBagSide();
        RepaintHandsSlot();
    }

    private void RepaintBagSide()
    {
        if (_interactor == null) return;
        var equipment = _interactor.CharacterEquipment;
        bool haveBag = equipment != null && equipment.HaveInventory();

        if (_bagGridRoot != null) _bagGridRoot.SetActive(haveBag);
        if (_noBagLabel != null) _noBagLabel.gameObject.SetActive(!haveBag);

        if (haveBag && _bagGrid != null)
        {
            _bagGrid.Bind(equipment.GetInventory().ItemSlots, OnBagSlotClicked, IsCharacterIdle);
        }
        else if (_bagGrid != null)
        {
            _bagGrid.Unbind();
        }
    }

    private void RepaintHandsSlot()
    {
        if (_interactor == null) return;
        var hands = _interactor.CharacterVisual?.BodyPartsController?.HandsController;
        ItemInstance carried = hands != null ? hands.CarriedItem : null;

        if (_handsSlotLabel != null)
        {
            _handsSlotLabel.text = carried != null
                ? carried.CustomizedName
                : "<color=#666666>(empty)</color>";
        }
        if (_handsSlotIcon != null)
        {
            if (carried != null && carried.ItemSO != null && carried.ItemSO.Icon != null)
            {
                _handsSlotIcon.sprite = carried.ItemSO.Icon;
                _handsSlotIcon.enabled = true;
            }
            else
            {
                _handsSlotIcon.enabled = false;
            }
        }
        RefreshHandsInteractable(carried);
    }

    private void RefreshHandsInteractable(ItemInstance carried)
    {
        if (_handsSlotButton == null) return;
        _handsSlotButton.interactable = (carried != null) && IsCharacterIdle();
    }

    private bool IsCharacterIdle()
    {
        if (_interactor == null) return false;
        return _interactor.CharacterActions != null
            && _interactor.CharacterActions.CurrentAction == null;
    }

    private void OnBagSlotClicked(ItemInstance item) => QueueStore(item);
    private void OnHandsSlotClicked()
    {
        if (_interactor == null) return;
        var hands = _interactor.CharacterVisual?.BodyPartsController?.HandsController;
        ItemInstance carried = hands != null ? hands.CarriedItem : null;
        QueueStore(carried);
    }

    private void QueueStore(ItemInstance item)
    {
        if (item == null || _target == null || _interactor == null) return;
        if (_interactor.CharacterActions == null) return;
        if (_interactor.CharacterActions.CurrentAction != null) return;

        var action = new CharacterStoreInFurnitureAction(_interactor, item, _target);
        _interactor.CharacterActions.ExecuteAction(action);
    }

    private void OnChestSlotClicked(ItemInstance item)
    {
        if (item == null || _target == null || _interactor == null) return;
        if (_interactor.CharacterActions == null) return;
        if (_interactor.CharacterActions.CurrentAction != null) return;

        var action = new CharacterTakeFromFurnitureAction(_interactor, item, _target);
        _interactor.CharacterActions.ExecuteAction(action);
    }
}
