using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IInspectorView for StorageFurniture targets (chest, shelf, barrel, wardrobe, ...). Renders a
/// header with the furniture name plus a slot-by-slot listing of the contained items along with
/// lock/full state. Refreshes every frame; cheap because DevInspectModule keeps non-active views
/// disabled.
///
/// Also exposes an "Inspect Parent Building" button that resolves the storage furniture's parent
/// <see cref="Building"/> and forwards it to <see cref="DevSelectionModule.SetSelectedBuilding"/>,
/// which in turn flips the inspector to the <see cref="BuildingInspectorView"/>. Lets you walk
/// from a piece of furniture up to its building without re-clicking through the world.
/// </summary>
public class StorageFurnitureInspectorView : MonoBehaviour, IInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content;

    [Header("Navigation")]
    [Tooltip("Reference to the Select tab module. Wired in the prefab inspector. Lets the 'Inspect Parent Building' button forward the parent Building to the selection slot.")]
    [SerializeField] private DevSelectionModule _selectionModule;

    [Tooltip("Optional. When set, clicking it switches the Inspect tab to the parent Building's inspector view. Greyed out when the storage has no Building ancestor.")]
    [SerializeField] private Button _inspectParentBuildingButton;

    [Tooltip("Optional. Label inside the inspect-parent button — used to surface the parent building's name when known.")]
    [SerializeField] private TMP_Text _inspectParentBuildingLabel;

    private const string PARENT_BUTTON_DEFAULT_LABEL = "Inspect Parent Building";

    private StorageFurniture _target;
    private Furniture _furniture;

    public bool CanInspect(InteractableObject target)
    {
        return target is FurnitureInteractable fi && fi.Furniture is StorageFurniture;
    }

    public void SetTarget(InteractableObject target)
    {
        if (target is FurnitureInteractable fi && fi.Furniture is StorageFurniture sf)
        {
            _target = sf;
            _furniture = fi.Furniture;
        }
        else
        {
            _target = null;
            _furniture = null;
        }
        UpdateHeader();
        RefreshParentBuildingButton();
    }

    public void Clear()
    {
        _target = null;
        _furniture = null;
        UpdateHeader();
        RefreshParentBuildingButton();
        if (_content != null) _content.text = "<color=grey>No storage selected.</color>";
    }

    private void Awake()
    {
        if (_inspectParentBuildingButton != null)
        {
            _inspectParentBuildingButton.onClick.AddListener(HandleInspectParentBuildingClicked);
        }
        RefreshParentBuildingButton();
    }

    private void OnDestroy()
    {
        if (_inspectParentBuildingButton != null)
        {
            _inspectParentBuildingButton.onClick.RemoveListener(HandleInspectParentBuildingClicked);
        }
    }

    private Building ResolveParentBuilding()
    {
        if (_furniture == null) return null;
        return _furniture.GetComponentInParent<Building>();
    }

    private void RefreshParentBuildingButton()
    {
        if (_inspectParentBuildingButton == null) return;

        Building parent = ResolveParentBuilding();
        bool hasParent = parent != null && _selectionModule != null;
        _inspectParentBuildingButton.interactable = hasParent;

        if (_inspectParentBuildingLabel == null) return;
        if (hasParent)
        {
            string label = !string.IsNullOrEmpty(parent.BuildingName)
                ? parent.BuildingName
                : parent.gameObject.name;
            _inspectParentBuildingLabel.text = $"→ Inspect {label}";
        }
        else
        {
            _inspectParentBuildingLabel.text = PARENT_BUTTON_DEFAULT_LABEL;
        }
    }

    private void HandleInspectParentBuildingClicked()
    {
        if (_selectionModule == null)
        {
            Debug.LogWarning("<color=orange>[StorageFurnitureInspectorView]</color> No DevSelectionModule wired — cannot navigate to parent building.");
            return;
        }
        Building parent = ResolveParentBuilding();
        if (parent == null)
        {
            Debug.LogWarning("<color=orange>[StorageFurnitureInspectorView]</color> Selected storage has no Building ancestor.");
            return;
        }
        _selectionModule.SetSelectedBuilding(parent);
    }

    private void Update()
    {
        if (_target == null || _content == null) return;
        try
        {
            _content.text = RenderContent(_target);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            _content.text = $"<color=red>⚠ {GetType().Name} failed — {e.Message}</color>";
        }
    }

    private void UpdateHeader()
    {
        if (_headerLabel == null) return;
        if (_target == null)
        {
            _headerLabel.text = "Inspecting: —";
            return;
        }
        string label = _furniture != null && !string.IsNullOrEmpty(_furniture.FurnitureName)
            ? _furniture.FurnitureName
            : _target.gameObject.name;
        _headerLabel.text = $"Inspecting: {label}";
    }

    private static string RenderContent(StorageFurniture s)
    {
        var slots = s.ItemSlots;
        var sb = new StringBuilder(512);

        // Counters for the summary line
        int filled = 0;
        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
                if (!slots[i].IsEmpty()) filled++;
        }

        sb.Append("<b>Capacity:</b> ").Append(filled).Append(" / ").Append(s.Capacity).AppendLine();
        sb.Append("<b>Locked:</b> ").AppendLine(s.IsLocked ? "<color=#FF6464>Yes</color>" : "<color=#64FF64>No</color>");
        sb.Append("<b>Full:</b> ").AppendLine(s.IsFull ? "Yes" : "No");
        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Slots</color></b>");

        if (slots == null || slots.Count == 0)
        {
            sb.AppendLine("<color=grey>(no slots)</color>");
            return sb.ToString();
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            string slotType = SlotTypeLabel(slot);
            sb.Append("  [").Append(i.ToString("00")).Append("] ");
            sb.Append("<color=#888888>").Append(slotType).Append("</color> — ");
            if (slot.IsEmpty())
            {
                sb.AppendLine("<color=grey>empty</color>");
            }
            else
            {
                sb.AppendLine(ItemDisplayName(slot.ItemInstance));
            }
        }

        return sb.ToString();
    }

    private static string SlotTypeLabel(ItemSlot slot)
    {
        switch (slot)
        {
            case WeaponSlot _:   return "Weapon";
            case WearableSlot _: return "Wearable";
            case MiscSlot _:     return "Misc";
            case AnySlot _:      return "Any";
            default:             return slot.GetType().Name;
        }
    }

    private static string ItemDisplayName(ItemInstance item)
    {
        if (item == null) return "<color=grey>null</color>";
        if (!string.IsNullOrEmpty(item.CustomizedName)) return item.CustomizedName;
        if (item.ItemSO != null && !string.IsNullOrEmpty(item.ItemSO.ItemName)) return item.ItemSO.ItemName;
        return item.GetType().Name;
    }
}
