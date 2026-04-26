using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactable attached to a <see cref="TimeClockFurniture"/>. When an
/// employee of the parent <see cref="CommercialBuilding"/> interacts, this
/// queues the existing <see cref="Action_PunchIn"/> or
/// <see cref="Action_PunchOut"/> CharacterAction depending on whether the
/// worker is already on shift — so the shared code path is used by both
/// players and NPCs (Rule #22).
///
/// Networking: player clients route through
/// <see cref="CommercialBuilding.RequestPunchAtTimeClockServerRpc"/>
/// because <c>WorkerStartingShift</c>/<c>WorkerEndingShift</c> mutate
/// server-only state (active-worker roster, quest auto-claim subscriptions,
/// WorkLog punch records). NPCs already live on the server, so they take
/// the direct path below.
/// </summary>
[RequireComponent(typeof(TimeClockFurniture))]
public class TimeClockFurnitureInteractable : FurnitureInteractable
{
    [Tooltip("Optional override. If null, resolved at Awake via GetComponentInParent<CommercialBuilding>().")]
    [SerializeField] private CommercialBuilding _building;

    [Tooltip("Toast message shown when a non-employee tries to punch here.")]
    [SerializeField] private string _notEmployeeMessage = "You don't work here.";

    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _timeClockToastChannel;

    private const string PROMPT_PUNCH_IN = "Press E to Punch In";
    private const string PROMPT_PUNCH_OUT = "Press E to Punch Out";
    private const string PROMPT_NOT_EMPLOYEE = "Staff only";

    public CommercialBuilding Building => _building;

    protected override void Awake()
    {
        base.Awake();
        if (_building == null)
        {
            _building = GetComponentInParent<CommercialBuilding>();
        }
        if (_building == null)
        {
            Debug.LogWarning($"<color=orange>[TimeClock]</color> {name} could not resolve a parent CommercialBuilding. Punching will no-op until wired up.", this);
        }
    }

    private void Update()
    {
        // Reactive prompt — only runs cheaply (string assignment guarded by equality).
        string desired = ResolveDesiredPrompt(null);
        if (interactionPrompt != desired) interactionPrompt = desired;
    }

    private string ResolveDesiredPrompt(Character contextInteractor)
    {
        if (_building == null) return "Press E to interact";

        if (contextInteractor != null && !_building.IsWorkerEmployedHere(contextInteractor))
        {
            return PROMPT_NOT_EMPLOYEE;
        }

        // Default prompt (no specific interactor yet): show Punch In since most of the
        // time the nearest character is off-shift. We refine live via GetHoldInteractionOptions.
        return PROMPT_PUNCH_IN;
    }

    public override void Interact(Character interactor)
    {
        if (interactor == null || _building == null) return;

        if (!_building.IsWorkerEmployedHere(interactor))
        {
            RaiseToast(string.Format("{0} — {1}", interactor.CharacterName, _notEmployeeMessage));
            return;
        }

        // Offline / single-player: NetworkManager absent → run directly.
        // Host / dedicated-server / NPC: already server-side → run directly.
        // Client player: hop through the building's ServerRpc so WorkerStartingShift
        // runs authoritatively on the server.
        var nm = Unity.Netcode.NetworkManager.Singleton;
        bool offline = nm == null || !nm.IsListening;
        if (offline || nm.IsServer)
        {
            RunPunchCycleServerSide(interactor);
        }
        else
        {
            if (string.IsNullOrEmpty(interactor.CharacterId))
            {
                Debug.LogWarning("[TimeClock] Cannot route punch ServerRpc — interactor has no CharacterId yet.");
                return;
            }
            _building.RequestPunchAtTimeClockServerRpc(interactor.CharacterId);
        }
    }

    /// <summary>
    /// Server-side entry point called by <see cref="Interact"/> on the server, and by
    /// <see cref="CommercialBuilding.RequestPunchAtTimeClockServerRpc"/> after
    /// re-resolving the worker. Centralises the furniture-lock + action-choice logic
    /// so players and NPCs share one code path.
    /// </summary>
    public void RunPunchCycleServerSide(Character worker)
    {
        if (worker == null || _building == null || Furniture == null) return;

        // If the clock is already in use (another worker is punching right now), let
        // Furniture's occupation gate reject quietly. The occupant flips back to null
        // when the previous worker's OnActionFinished fires.
        if (Furniture.IsOccupied && Furniture.Occupant != worker) return;

        if (!Furniture.IsOccupied && !Furniture.Use(worker)) return;

        // Read the replicated roster — same truth on server and clients, and
        // consistent with the UI prompt / hold-menu the client sees.
        bool onShift = _building.IsWorkerOnShift(worker);
        CharacterAction action = onShift
            ? (CharacterAction)new Action_PunchOut(worker, _building)
            : new Action_PunchIn(worker, _building);

        if (worker.CharacterActions == null || !worker.CharacterActions.ExecuteAction(action))
        {
            Furniture.Release();
            return;
        }

        // Release the clock as soon as the punch animation completes so the next
        // worker can queue. Defensive unsubscribe — multi-fire via Action.Finish()
        // shouldn't happen, but guarding against it costs nothing.
        System.Action releaseOnce = null;
        releaseOnce = () =>
        {
            action.OnActionFinished -= releaseOnce;
            if (Furniture != null && Furniture.Occupant == worker) Furniture.Release();
        };
        action.OnActionFinished += releaseOnce;
    }

    public override List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        // Start with whatever the base class exposes (e.g. "Pick Up" for authored FurnitureItemSO).
        var baseOptions = base.GetHoldInteractionOptions(interactor) ?? new List<InteractionOption>();

        if (interactor == null || _building == null) return baseOptions.Count > 0 ? baseOptions : null;

        if (!_building.IsWorkerEmployedHere(interactor))
        {
            baseOptions.Add(new InteractionOption
            {
                Name = _notEmployeeMessage,
                IsDisabled = true,
                Action = null
            });
            return baseOptions;
        }

        bool onShift = _building.IsWorkerOnShift(interactor);
        baseOptions.Insert(0, new InteractionOption
        {
            Name = onShift ? "Punch Out" : "Punch In",
            IsDisabled = false,
            Action = () => Interact(interactor)
        });

        return baseOptions;
    }

    private void RaiseToast(string message)
    {
        if (_timeClockToastChannel == null) return;
        _timeClockToastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
            message: message,
            type: MWI.UI.Notifications.ToastType.Warning,
            duration: 2.5f
        ));
    }
}
