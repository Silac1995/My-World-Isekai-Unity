using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable attached to a <see cref="BedFurniture"/>. When the local player
/// uses the bed, opens <see cref="UI_BedSleepPrompt"/> for hour selection.
/// On confirm, routes through <see cref="CharacterActions.RequestSleepOnFurnitureServerRpc"/>
/// so the server runs <c>bed.UseSlot</c> + sets PendingSkipHours + enqueues
/// <see cref="CharacterAction_SleepOnFurniture"/>. The TimeSkipController auto-trigger
/// watcher then fires the skip.
///
/// For NPCs (server direct path), the existing <see cref="SleepBehaviour"/> still
/// drives sleep — this interactable is the player surface only.
/// </summary>
[RequireComponent(typeof(BedFurniture))]
public class BedFurnitureInteractable : FurnitureInteractable
{
    [Header("Bed UI")]
    [Tooltip("Bed sleep prompt UI singleton. If null, resolved via Object.FindFirstObjectByType at first interact.")]
    [SerializeField] private UI_BedSleepPrompt _sleepPrompt;

    [Tooltip("Override if this interactable lives nested under a non-default networked ancestor.")]
    [SerializeField] private NetworkObject _parentNetworkObject;

    private BedFurniture _bed;

    private const string PROMPT_DEFAULT = "Press E to Sleep";
    private const string PROMPT_OCCUPIED = "Bed is full";

    protected override void Awake()
    {
        base.Awake();

        _bed = GetComponent<BedFurniture>();

        if (_parentNetworkObject == null)
        {
            // Beds live nested under a CommercialBuilding / ResidentialBuilding /
            // similar — walk up to the nearest NetworkObject ancestor.
            _parentNetworkObject = GetComponentInParent<NetworkObject>();
        }
    }

    private void Update()
    {
        // Reactive prompt — only runs cheaply (string assignment guarded by equality).
        string desired = _bed != null && _bed.HasFreeSlot ? PROMPT_DEFAULT : PROMPT_OCCUPIED;
        if (interactionPrompt != desired) interactionPrompt = desired;
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _bed == null) return;

        // Resolve a free slot up-front (needed by both player UI and direct server path).
        int slotIndex = _bed.GetSlotIndexFor(interactor);
        if (slotIndex < 0) slotIndex = _bed.FindFreeSlotIndex();
        if (slotIndex < 0)
        {
            Debug.Log($"<color=orange>[Bed]</color> No free slot on {_bed.FurnitureName} for {interactor.CharacterName}.");
            return;
        }

        // Branch: local-player → UI prompt; everyone else (NPC / direct server) → enqueue immediately.
        bool isLocalPlayer = interactor.IsOwner && interactor.IsPlayer();

        if (isLocalPlayer)
        {
            ShowPromptForLocalPlayer(interactor, slotIndex);
        }
        else
        {
            EnqueueSleepServerSide(interactor, slotIndex, desiredHours: 0);
        }
    }

    private void ShowPromptForLocalPlayer(Character localPlayer, int slotIndex)
    {
        if (_sleepPrompt == null)
        {
            _sleepPrompt = Object.FindFirstObjectByType<UI_BedSleepPrompt>(FindObjectsInactive.Include);
        }

        if (_sleepPrompt == null)
        {
            Debug.LogWarning("<color=orange>[Bed]</color> No UI_BedSleepPrompt found in scene. Skipping prompt.");
            return;
        }

        // The prompt's Confirm callback drives the actual server enqueue.
        _sleepPrompt.Show(hours =>
        {
            if (localPlayer == null || _bed == null) return;
            EnqueueSleep(localPlayer, slotIndex, hours);
        });
    }

    private void EnqueueSleep(Character character, int slotIndex, int desiredHours)
    {
        var nm = NetworkManager.Singleton;
        bool offline = nm == null || !nm.IsListening;
        if (offline || nm.IsServer)
        {
            EnqueueSleepServerSide(character, slotIndex, desiredHours);
            return;
        }

        if (_parentNetworkObject == null)
        {
            Debug.LogError("<color=red>[Bed]</color> Cannot route ServerRpc — no parent NetworkObject resolved.");
            return;
        }

        character.CharacterActions?.RequestSleepOnFurnitureServerRpc(
            new NetworkObjectReference(_parentNetworkObject),
            _bed.transform.position,
            slotIndex,
            desiredHours);
    }

    private void EnqueueSleepServerSide(Character character, int slotIndex, int desiredHours)
    {
        if (desiredHours > 0)
        {
            character.SetPendingSkipHours(desiredHours);
        }

        var action = new CharacterAction_SleepOnFurniture(character, _bed, slotIndex);
        if (character.CharacterActions == null || !character.CharacterActions.ExecuteAction(action))
        {
            Debug.LogWarning($"<color=orange>[Bed]</color> ExecuteAction rejected for {character.CharacterName} on {_bed.FurnitureName}.");
        }
    }

    public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        var baseOptions = base.GetHoldInteractionOptions(interactor) ?? new List<InteractionOption>();

        if (interactor == null || _bed == null) return baseOptions.Count > 0 ? baseOptions : null;

        bool hasFree = _bed.HasFreeSlot;
        bool isOccupant = _bed.GetSlotIndexFor(interactor) >= 0;

        baseOptions.Insert(0, new InteractionOption
        {
            Name = isOccupant ? "Wake up" : "Sleep",
            IsDisabled = !isOccupant && !hasFree,
            Action = () => Interact(interactor)
        });

        return baseOptions;
    }
}
