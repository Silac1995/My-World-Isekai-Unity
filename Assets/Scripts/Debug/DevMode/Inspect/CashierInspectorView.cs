using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// IInspectorView for <see cref="Cashier"/> targets — the customer-facing transaction
/// counter inside a CommercialBuilding (today only ShopBuilding uses it). Mirrors
/// <see cref="StorageFurnitureInspectorView"/> in shape: header + scrollable content
/// block + optional "Inspect Parent Building" nav button that forwards to
/// <see cref="DevSelectionModule.SetSelectedBuilding"/>.
///
/// Accepts both selection routes:
///   • <see cref="CashierInteractable"/> — the dedicated InteractableObject component
///     on a Cashier (the normal Ctrl+Click pick path).
///   • <see cref="FurnitureInteractable"/> wrapping a Cashier — defensive fallback in
///     case a prefab variant uses the generic furniture interactable.
///
/// Refreshes every frame; cheap because <see cref="DevInspectModule"/> keeps non-active
/// views disabled. Reads till balances from the replicated
/// <see cref="CashierNetSync.TillBalances"/> NetworkList so the panel is meaningful on
/// every peer (the server-only <c>_till</c> dictionary is invisible to clients).
/// </summary>
public class CashierInspectorView : MonoBehaviour, IInspectorView
{
    [Header("Labels")]
    [SerializeField] private TMP_Text _headerLabel;
    [SerializeField] private TMP_Text _content;

    [Header("Navigation")]
    [Tooltip("Reference to the Select tab module. Wired in the prefab inspector. Lets the 'Inspect Parent Building' button forward the parent Building to the selection slot.")]
    [SerializeField] private DevSelectionModule _selectionModule;

    [Tooltip("Optional. When set, clicking it switches the Inspect tab to the parent Building's inspector view. Greyed out when the cashier has no Building ancestor.")]
    [SerializeField] private Button _inspectParentBuildingButton;

    [Tooltip("Optional. Label inside the inspect-parent button — used to surface the parent building's name when known.")]
    [SerializeField] private TMP_Text _inspectParentBuildingLabel;

    private const string PARENT_BUTTON_DEFAULT_LABEL = "Inspect Parent Building";

    private Cashier _target;

    public bool CanInspect(InteractableObject target)
    {
        if (target == null) return false;
        if (target is CashierInteractable) return true;
        if (target is FurnitureInteractable fi && fi.Furniture is Cashier) return true;
        return false;
    }

    public void SetTarget(InteractableObject target)
    {
        _target = ResolveCashier(target);
        UpdateHeader();
        RefreshParentBuildingButton();
    }

    public void Clear()
    {
        _target = null;
        UpdateHeader();
        RefreshParentBuildingButton();
        if (_content != null) _content.text = "<color=grey>No cashier selected.</color>";
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

    private static Cashier ResolveCashier(InteractableObject target)
    {
        if (target == null) return null;
        if (target is CashierInteractable ci) return ci.GetComponent<Cashier>();
        if (target is FurnitureInteractable fi && fi.Furniture is Cashier c) return c;
        return null;
    }

    private Building ResolveParentBuilding()
    {
        if (_target == null) return null;
        if (_target.LinkedBuilding != null) return _target.LinkedBuilding;
        return _target.GetComponentInParent<Building>();
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
            Debug.LogWarning("<color=orange>[CashierInspectorView]</color> No DevSelectionModule wired — cannot navigate to parent building.");
            return;
        }
        Building parent = ResolveParentBuilding();
        if (parent == null)
        {
            Debug.LogWarning("<color=orange>[CashierInspectorView]</color> Selected cashier has no Building ancestor.");
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
        string label = !string.IsNullOrEmpty(_target.FurnitureName)
            ? _target.FurnitureName
            : _target.gameObject.name;
        _headerLabel.text = $"Inspecting: {label}";
    }

    // ─── Rendering ────────────────────────────────────────────────────────

    private static string RenderContent(Cashier c)
    {
        var sb = new StringBuilder(1024);

        AppendIdentity(sb, c);
        sb.AppendLine();
        AppendCashierState(sb, c);
        sb.AppendLine();
        AppendTill(sb, c);
        sb.AppendLine();
        AppendLinkedBuilding(sb, c);
        sb.AppendLine();
        AppendNetworkSync(sb, c);
        sb.AppendLine();
        AppendActiveAction(sb, c);

        return sb.ToString();
    }

    private static void AppendIdentity(StringBuilder sb, Cashier c)
    {
        sb.AppendLine("<b><color=#FFFFFF>Identity</color></b>");
        sb.Append("  Type: <color=#88CCFF>").Append(c.GetType().Name).AppendLine("</color>");
        sb.Append("  GameObject: ").AppendLine(c.gameObject.name);
        sb.Append("  Furniture name: ").AppendLine(string.IsNullOrEmpty(c.FurnitureName) ? "<color=grey>(none)</color>" : c.FurnitureName);
        sb.Append("  Layer: ").Append(LayerMask.LayerToName(c.gameObject.layer))
          .Append(" (").Append(c.gameObject.layer).AppendLine(")");
        Vector3 p = c.transform.position;
        sb.AppendFormat("  Position: ({0:0.00}, {1:0.00}, {2:0.00})\n", p.x, p.y, p.z);
    }

    private static void AppendCashierState(StringBuilder sb, Cashier c)
    {
        sb.AppendLine("<b><color=#FFFFFF>Cashier state</color></b>");
        sb.Append("  Requires vendor: ").AppendLine(BoolColor(c.RequiresVendor));
        sb.Append("  Occupied (vendor seated): ").AppendLine(BoolColor(c.IsOccupied));
        sb.Append("  Vendor: ").AppendLine(CharacterDisplay(c.Occupant));
        sb.Append("  Current customer: ").AppendLine(CharacterDisplay(c.CurrentCustomer));
        sb.Append("  Available for customer: ").AppendLine(BoolColor(c.IsAvailableForCustomer));
    }

    private static void AppendTill(StringBuilder sb, Cashier c)
    {
        sb.AppendLine("<b><color=#FFFFFF>Till</color></b>");

        var netSync = c.NetSync;
        bool useNetList = netSync != null && netSync.IsSpawned && netSync.TillBalances != null;

        if (useNetList)
        {
            int count = netSync.TillBalances.Count;
            if (count == 0)
            {
                sb.AppendLine("  <color=grey>(empty)</color>");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var entry = netSync.TillBalances[i];
                    sb.Append("  • Currency #").Append(entry.CurrencyId)
                      .Append(": ").Append(entry.Amount).AppendLine();
                }
            }
            sb.AppendLine("  <color=#888888>(replicated via CashierNetSync.TillBalances)</color>");
        }
        else
        {
            // Fallback: server-side in-memory dictionary. Clients reach this branch only
            // when NetSync is not yet spawned (very early in the bind / spawn race).
            var balances = c.GetAllTillBalances();
            if (balances == null || balances.Count == 0)
            {
                sb.AppendLine("  <color=grey>(empty — NetSync not yet spawned)</color>");
            }
            else
            {
                foreach (var kv in balances)
                {
                    sb.Append("  • ").Append(kv.Key.Id).Append(": ").Append(kv.Value).AppendLine();
                }
            }
        }
    }

    private static void AppendLinkedBuilding(StringBuilder sb, Cashier c)
    {
        sb.AppendLine("<b><color=#FFFFFF>Linked Building</color></b>");
        var linked = c.LinkedBuilding;
        if (linked == null)
        {
            sb.AppendLine("  <color=#FF6464>(unresolved)</color>");
            return;
        }
        sb.Append("  Type: <color=#88CCFF>").Append(linked.GetType().Name).AppendLine("</color>");
        sb.Append("  Name: ").AppendLine(string.IsNullOrEmpty(linked.BuildingName) ? linked.gameObject.name : linked.BuildingName);
        sb.Append("  Building id: ").AppendLine(string.IsNullOrEmpty(linked.BuildingId) ? "<color=grey>(none)</color>" : linked.BuildingId);
        var shop = c.LinkedShop;
        sb.Append("  Is ShopBuilding: ").AppendLine(BoolColor(shop != null));
    }

    private static void AppendNetworkSync(StringBuilder sb, Cashier c)
    {
        sb.AppendLine("<b><color=#FFFFFF>Network sync</color></b>");
        var netSync = c.NetSync;
        if (netSync == null)
        {
            sb.AppendLine("  <color=#FF6464>CashierNetSync missing.</color>");
            return;
        }

        sb.Append("  Spawned: ").AppendLine(BoolColor(netSync.IsSpawned));
        sb.Append("  IsServer (this peer): ").AppendLine(BoolColor(netSync.IsServer));

        var ownNo = netSync.GetComponent<NetworkObject>();
        if (ownNo != null)
        {
            sb.Append("  NetworkObjectId: ").Append(ownNo.NetworkObjectId)
              .Append(ownNo.IsSpawned ? " <color=#64FF64>(spawned)</color>" : " <color=grey>(not spawned)</color>")
              .AppendLine();
        }
        else
        {
            sb.AppendLine("  NetworkObject: <color=grey>none</color>");
        }

        sb.Append("  OccupantNetworkObjectId: ").AppendLine(NetIdString(netSync.OccupantNetworkObjectId.Value));
        sb.Append("  CurrentCustomerNetworkObjectId: ").AppendLine(NetIdString(netSync.CurrentCustomerNetworkObjectId.Value));

        bool linkedRefValid = netSync.LinkedBuildingRef.Value.TryGet(out NetworkObject linkedNo);
        if (linkedRefValid && linkedNo != null)
        {
            sb.Append("  LinkedBuildingRef: NetworkObjectId=").Append(linkedNo.NetworkObjectId).AppendLine();
        }
        else
        {
            sb.AppendLine("  LinkedBuildingRef: <color=grey>(unset / unresolved)</color>");
        }

        sb.Append("  Till entries (replicated): ").Append(netSync.TillBalances != null ? netSync.TillBalances.Count : 0).AppendLine();
    }

    private static void AppendActiveAction(StringBuilder sb, Cashier c)
    {
        sb.AppendLine("<b><color=#FFFFFF>Active action</color></b>");
        var netSync = c.NetSync;
        if (netSync == null)
        {
            sb.AppendLine("  <color=grey>(no NetSync)</color>");
            return;
        }

        var action = netSync.ActiveAction;
        if (action == null)
        {
            sb.AppendLine("  <color=grey>(no transaction in flight on this peer)</color>");
            // Only the server actually tracks ActiveAction — surface the hint so clients
            // are not confused by the empty section.
            if (!netSync.IsServer)
            {
                sb.AppendLine("  <color=#888888>(ActiveAction is server-only — clients see this null even mid-transaction.)</color>");
            }
            return;
        }

        sb.Append("  Mode: ").AppendLine(action.Mode.ToString());
        sb.Append("  Customer: ").AppendLine(CharacterDisplay(c.CurrentCustomer));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static string CharacterDisplay(Character ch)
    {
        if (ch == null) return "<color=grey>(none)</color>";
        if (!string.IsNullOrEmpty(ch.CharacterName)) return ch.CharacterName;
        return ch.gameObject.name;
    }

    private static string NetIdString(ulong id)
    {
        if (id == 0) return "<color=grey>0 (unset)</color>";
        return id.ToString();
    }

    private static string BoolColor(bool value)
    {
        string color = value ? "#64FF64" : "#FF6464";
        return $"<color={color}>{(value ? "Yes" : "No")}</color>";
    }
}
