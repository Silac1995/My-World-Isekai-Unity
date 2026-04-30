using UnityEngine;

/// <summary>
/// Generic placard / signboard / notice-board furniture. Holds server-authoritative text
/// (replicated via the sibling <see cref="DisplayTextFurnitureNetSync"/> component). Any
/// player or NPC can interact with it -> reads the text. Owner of the parent
/// <see cref="CommercialBuilding"/> can edit the text via <see cref="TrySetDisplayText"/>.
///
/// Mirror of <see cref="StorageFurniture"/> + <see cref="StorageFurnitureNetworkSync"/>:
/// gameplay-data on a Furniture MonoBehaviour, replication on a sibling NetworkBehaviour.
/// Both share the same GameObject + NetworkObject from the Furniture_prefab base.
/// </summary>
public class DisplayTextFurniture : Furniture
{
    [Header("Display Text")]
    [Tooltip("Authoring-time default text. Used as the initial DisplayText on first spawn.")]
    [TextArea(2, 8)]
    [SerializeField] private string _initialText = "";

    private DisplayTextFurnitureNetSync _netSync;

    public string InitialText => _initialText;
    public DisplayTextFurnitureNetSync NetSync => _netSync;

    /// <summary>Current displayed text. Server-authoritative; replicates via NetSync.</summary>
    public string DisplayText => _netSync != null ? _netSync.DisplayText : _initialText;

    /// <summary>Fires whenever <see cref="DisplayText"/> changes (server + clients).</summary>
    public event System.Action<string> OnDisplayTextChanged;

    protected virtual void Awake()
    {
        _netSync = GetComponent<DisplayTextFurnitureNetSync>();
        if (_netSync != null)
        {
            _netSync.OnDisplayTextChanged += HandleNetSyncTextChanged;
        }
        else
        {
            Debug.LogError($"[DisplayTextFurniture] {name} has no sibling DisplayTextFurnitureNetSync — text will not replicate.");
        }
    }

    protected virtual void OnDestroy()
    {
        if (_netSync != null)
            _netSync.OnDisplayTextChanged -= HandleNetSyncTextChanged;
    }

    private void HandleNetSyncTextChanged(string newText) => OnDisplayTextChanged?.Invoke(newText);

    /// <summary>
    /// Owner-gated text mutation. Routes via NetSync ServerRpc when called from a client.
    /// Returns true if the mutation succeeded (host-side direct write, or client-side
    /// optimistic acceptance — actual write is server-authoritative).
    /// </summary>
    public bool TrySetDisplayText(Character requester, string newText)
    {
        if (_netSync == null) return false;
        return _netSync.TrySetDisplayText(requester, newText);
    }

    /// <summary>
    /// A sign is a "read-only" furniture — interacting with it opens the reader UI for the
    /// local player; for NPCs it's a silent success (they have no need to "read" text).
    ///
    /// We deliberately bypass the base <see cref="Furniture.Use"/> occupant/reservation logic:
    /// many players can read the same sign simultaneously, and the sign is never "occupied"
    /// like a chair or workstation. The canonical entry path is
    /// <see cref="FurnitureInteractable.Interact"/> → <c>_furniture.Use(interactor)</c>, which
    /// is shared by every furniture type — overriding here keeps `DisplayTextFurniture` plug-
    /// compatible with the existing PlayerInteractionDetector E-press flow without needing a
    /// dedicated sibling InteractableObject subclass.
    /// </summary>
    public override bool Use(Character character)
    {
        if (character == null) return false;
        if (!character.IsPlayer()) return true; // NPCs: silent success, no UI need

        // Local-player gate — remote player Characters also have a PlayerController, so
        // `character.IsPlayer()` returns true on every machine. Only the OWNER of this
        // Character should see the UI; otherwise every client would pop a reader when any
        // remote player presses E on a sign.
        if (character.IsSpawned && !character.IsOwner) return true;

        UI_DisplayTextReader.Show(this);
        return true;
    }
}
