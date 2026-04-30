using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative replication for <see cref="DisplayTextFurniture"/>.
/// Mirrors <see cref="StorageFurnitureNetworkSync"/>'s sibling-NetworkBehaviour pattern:
/// the Furniture base is a plain MonoBehaviour, so the NetworkVariable lives on this
/// sibling component. Both share the same GameObject + NetworkObject from the
/// Furniture_prefab base.
///
/// Authority: server-only writer (NetworkVariableWritePermission.Server); everyone reads.
/// Client mutation requests route through TrySetDisplayText -> owner-authority check ->
/// ServerRpc.
///
/// Late joiners: NetworkVariable auto-syncs current value during spawn handshake.
/// </summary>
[RequireComponent(typeof(DisplayTextFurniture))]
public class DisplayTextFurnitureNetSync : NetworkBehaviour
{
    private DisplayTextFurniture _furniture;

    private NetworkVariable<FixedString512Bytes> _displayText = new(
        new FixedString512Bytes(),
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);

    public string DisplayText => _displayText.Value.ToString();
    public event System.Action<string> OnDisplayTextChanged;

    private void Awake()
    {
        _furniture = GetComponent<DisplayTextFurniture>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Server: seed from authoring _initialText if empty (first spawn / fresh save).
        if (IsServer && string.IsNullOrEmpty(_displayText.Value.ToString()))
        {
            string seed = _furniture != null ? _furniture.InitialText : "";
            if (!string.IsNullOrEmpty(seed))
                _displayText.Value = SanitiseAndClamp(seed);
        }

        _displayText.OnValueChanged += HandleNetVarChanged;
        OnDisplayTextChanged?.Invoke(_displayText.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        _displayText.OnValueChanged -= HandleNetVarChanged;
        base.OnNetworkDespawn();
    }

    private void HandleNetVarChanged(FixedString512Bytes _, FixedString512Bytes newVal)
    {
        OnDisplayTextChanged?.Invoke(newVal.ToString());
    }

    /// <summary>
    /// Owner-gated text mutation. Validates the requester has authority over the parent
    /// CommercialBuilding (via CanRequesterControlHiring — added in Plan 2 Task 3). Returns
    /// true if the mutation succeeded. Routes via ServerRpc when called from a client.
    /// </summary>
    public bool TrySetDisplayText(Character requester, string newText)
    {
        var building = GetComponentInParent<CommercialBuilding>();
        if (building == null)
        {
            Debug.LogWarning($"[DisplayTextFurniture] {name} not parented under a CommercialBuilding; mutations rejected.");
            return false;
        }

        // Server side: validate + write directly.
        if (IsServer)
        {
            if (requester != null && !building.CanRequesterControlHiring(requester))
                return false;
            _displayText.Value = SanitiseAndClamp(newText);
            return true;
        }

        // Client side: route to server. Validation happens server-side too.
        TrySetDisplayTextServerRpc(SanitiseAndClamp(newText), requester != null ? requester.NetworkObjectId : 0);
        return true; // optimistic; actual write is server-authoritative
    }

    [Rpc(SendTo.Server)]
    private void TrySetDisplayTextServerRpc(FixedString512Bytes newText, ulong requesterNetId, RpcParams rpcParams = default)
    {
        var building = GetComponentInParent<CommercialBuilding>();
        if (building == null) return;

        Character requester = ResolveCharacter(requesterNetId);
        if (requester != null && !building.CanRequesterControlHiring(requester))
            return;

        _displayText.Value = newText;
    }

    private static Character ResolveCharacter(ulong netId)
    {
        if (netId == 0 || NetworkManager.Singleton == null) return null;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj)) return null;
        return netObj != null ? netObj.GetComponent<Character>() : null;
    }

    /// <summary>
    /// Unrestricted server-only setter — used internally by the parent CommercialBuilding
    /// when its hiring state changes (Plan 2 Task 4). NOT callable from client RPCs.
    /// </summary>
    internal void ServerSetDisplayText(string newText)
    {
        if (!IsServer)
        {
            Debug.LogError("[DisplayTextFurniture] ServerSetDisplayText called from client — ignored.");
            return;
        }
        _displayText.Value = SanitiseAndClamp(newText);
    }

    private static FixedString512Bytes SanitiseAndClamp(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return new FixedString512Bytes();

        // Strip control chars (preserve \n / \t).
        var sb = new System.Text.StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '\n' || c == '\t') { sb.Append(c); continue; }
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }
        string clean = sb.ToString();

        // Clamp to ~480 UTF-8 bytes to leave headroom inside the 512-byte FixedString.
        const int maxBytes = 480;
        if (System.Text.Encoding.UTF8.GetByteCount(clean) > maxBytes)
        {
            int maxChars = maxBytes;
            while (maxChars > 0 && System.Text.Encoding.UTF8.GetByteCount(clean.Substring(0, System.Math.Min(maxChars, clean.Length))) > maxBytes)
                maxChars--;
            if (maxChars > 0) clean = clean.Substring(0, maxChars);
        }

        return new FixedString512Bytes(clean);
    }
}
