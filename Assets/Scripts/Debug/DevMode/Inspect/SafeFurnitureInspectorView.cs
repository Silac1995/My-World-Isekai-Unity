using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MWI.Economy;

/// <summary>
/// IInspectorView for <see cref="SafeFurniture"/> targets. Sibling of
/// <see cref="StorageFurnitureInspectorView"/> — same shape (header + content +
/// "Inspect Parent Building" navigation button) but renders the safe's role
/// + per-<see cref="CurrencyId"/> balance instead of slot listings, since a
/// safe holds currency, not items.
///
/// Auto-discovered by <see cref="DevInspectModule.CollectViews"/> as long as the
/// component lives somewhere under the Inspect tab content. Refreshes every
/// frame from <see cref="SafeFurniture.Balances"/> + <see cref="SafeFurniture.Role"/>
/// — cheap because <see cref="DevInspectModule"/> keeps non-active views disabled.
/// </summary>
public class SafeFurnitureInspectorView : MonoBehaviour, IInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content;

    [Header("Navigation")]
    [Tooltip("Reference to the Select tab module. Wired in the prefab inspector. Lets the 'Inspect Parent Building' button forward the parent Building to the selection slot.")]
    [SerializeField] private DevSelectionModule _selectionModule;

    [Tooltip("Optional. When set, clicking it switches the Inspect tab to the parent Building's inspector view. Greyed out when the safe has no Building ancestor.")]
    [SerializeField] private Button _inspectParentBuildingButton;

    [Tooltip("Optional. Label inside the inspect-parent button — used to surface the parent building's name when known.")]
    [SerializeField] private TMP_Text _inspectParentBuildingLabel;

    private const string PARENT_BUTTON_DEFAULT_LABEL = "Inspect Parent Building";

    private SafeFurniture _target;
    private Furniture _furniture;

    public bool CanInspect(InteractableObject target)
    {
        return target is FurnitureInteractable fi && fi.Furniture is SafeFurniture;
    }

    public void SetTarget(InteractableObject target)
    {
        if (target is FurnitureInteractable fi && fi.Furniture is SafeFurniture sf)
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
        if (_content != null) _content.text = "<color=grey>No safe selected.</color>";
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
            Debug.LogWarning("<color=orange>[SafeFurnitureInspectorView]</color> No DevSelectionModule wired — cannot navigate to parent building.");
            return;
        }
        Building parent = ResolveParentBuilding();
        if (parent == null)
        {
            Debug.LogWarning("<color=orange>[SafeFurnitureInspectorView]</color> Selected safe has no Building ancestor.");
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

    // ── Currency-name reflection (mirror of EconomySubTab.GetKnownCurrencies) ──

    private static List<(string name, int id)> _knownCurrencies;

    private static List<(string name, int id)> GetKnownCurrencies()
    {
        if (_knownCurrencies != null) return _knownCurrencies;

        var list = new List<(string name, int id)>();
        foreach (var f in typeof(CurrencyId).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.FieldType != typeof(CurrencyId)) continue;
            object raw = f.GetValue(null);
            if (raw is CurrencyId id) list.Add((f.Name, id.Id));
        }
        _knownCurrencies = list;
        return _knownCurrencies;
    }

    private static string CurrencyName(int id)
    {
        var known = GetKnownCurrencies();
        for (int i = 0; i < known.Count; i++)
        {
            if (known[i].id == id) return known[i].name;
        }
        return $"Currency#{id}";
    }

    private static string RoleLabel(SafeRoleType role)
    {
        switch (role)
        {
            case SafeRoleType.Treasury: return "<color=#FFD870>Treasury</color>";
            case SafeRoleType.None:     return "<color=grey>None</color>";
            default:                    return role.ToString();
        }
    }

    private static string RenderContent(SafeFurniture s)
    {
        var sb = new StringBuilder(256);
        sb.Append("<b>Role:</b> ").AppendLine(RoleLabel(s.Role));

        var balances = s.Balances;
        int totalCoins = 0;
        int entryCount = balances?.Count ?? 0;
        if (balances != null)
        {
            for (int i = 0; i < balances.Count; i++) totalCoins += balances[i].Amount;
        }

        sb.Append("<b>Balance entries:</b> ").AppendLine(entryCount.ToString());
        sb.Append("<b>Total coins:</b> ").AppendLine(totalCoins.ToString("N0"));
        sb.AppendLine();
        sb.AppendLine("<b><color=#FFFFFF>Currencies</color></b>");

        if (balances == null || balances.Count == 0)
        {
            sb.AppendLine("<color=grey>(no balance)</color>");
            return sb.ToString();
        }

        for (int i = 0; i < balances.Count; i++)
        {
            var e = balances[i];
            sb.Append("  · <color=#888888>")
              .Append(CurrencyName(e.CurrencyId))
              .Append("</color> — ");
            sb.AppendLine(e.Amount.ToString("N0"));
        }

        return sb.ToString();
    }
}
