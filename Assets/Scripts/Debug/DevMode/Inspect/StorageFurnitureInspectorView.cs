using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// IInspectorView for StorageFurniture targets (chest, shelf, barrel, wardrobe, ...). Renders a
/// header with the furniture name plus a slot-by-slot listing of the contained items along with
/// lock/full state. Refreshes every frame; cheap because DevInspectModule keeps non-active views
/// disabled.
/// </summary>
public class StorageFurnitureInspectorView : MonoBehaviour, IInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content;

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
    }

    public void Clear()
    {
        _target = null;
        _furniture = null;
        UpdateHeader();
        if (_content != null) _content.text = "<color=grey>No storage selected.</color>";
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
