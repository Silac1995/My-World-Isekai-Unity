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
        Debug.Log($"<color=cyan>[TimeClock:Interact]</color> entry. interactor={(interactor != null ? interactor.CharacterName : "<null>")}, _building={(_building != null ? _building.BuildingName : "<null>")}.");

        if (interactor == null || _building == null)
        {
            Debug.LogWarning($"<color=orange>[TimeClock:Interact]</color> aborted — interactor or building is null.");
            return;
        }

        bool employed = _building.IsWorkerEmployedHere(interactor);
        Debug.Log($"<color=cyan>[TimeClock:Interact]</color> IsWorkerEmployedHere({interactor.CharacterName}) = {employed}. interactor.CharacterId='{interactor.CharacterId}'.");
        if (!employed)
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
        Debug.Log($"<color=cyan>[TimeClock:Interact]</color> network state: offline={offline}, IsServer={(nm != null && nm.IsServer)}, IsClient={(nm != null && nm.IsClient)}, IsHost={(nm != null && nm.IsHost)}, IsListening={(nm != null && nm.IsListening)}.");
        if (offline || nm.IsServer)
        {
            Debug.Log($"<color=cyan>[TimeClock:Interact]</color> taking SERVER-SIDE path → RunPunchCycleServerSide.");
            RunPunchCycleServerSide(interactor);
        }
        else
        {
            if (string.IsNullOrEmpty(interactor.CharacterId))
            {
                Debug.LogWarning("[TimeClock:Interact] Cannot route punch ServerRpc — interactor has no CharacterId yet.");
                return;
            }
            Debug.Log($"<color=cyan>[TimeClock:Interact]</color> taking CLIENT path → sending RequestPunchAtTimeClockServerRpc(workerId='{interactor.CharacterId}') to building '{_building.BuildingName}'.");
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
        Debug.Log($"<color=magenta>[TimeClock:RunCycle]</color> entry. worker={(worker != null ? worker.CharacterName : "<null>")}, building={(_building != null ? _building.BuildingName : "<null>")}, Furniture={(Furniture != null ? Furniture.FurnitureName : "<null>")}.");
        if (worker == null || _building == null || Furniture == null)
        {
            Debug.LogWarning($"<color=orange>[TimeClock:RunCycle]</color> aborted — null guard. worker={(worker == null ? "NULL" : "ok")}, _building={(_building == null ? "NULL" : "ok")}, Furniture={(Furniture == null ? "NULL" : "ok")}.");
            return;
        }

        // If the clock is already in use (another worker is punching right now), let
        // Furniture's occupation gate reject quietly. The occupant flips back to null
        // when the previous worker's OnActionFinished fires.
        if (Furniture.IsOccupied && Furniture.Occupant != worker)
        {
            Debug.LogWarning($"<color=orange>[TimeClock:RunCycle]</color> aborted — clock already occupied by '{Furniture.Occupant.CharacterName}', not '{worker.CharacterName}'.");
            return;
        }

        if (!Furniture.IsOccupied && !Furniture.Use(worker))
        {
            Debug.LogWarning($"<color=orange>[TimeClock:RunCycle]</color> aborted — Furniture.Use({worker.CharacterName}) returned false.");
            return;
        }

        // Read the replicated roster — same truth on server and clients, and
        // consistent with the UI prompt / hold-menu the client sees.
        bool onShift = _building.IsWorkerOnShift(worker);
        Debug.Log($"<color=magenta>[TimeClock:RunCycle]</color> shift state: IsWorkerOnShift({worker.CharacterName})={onShift}. Queuing {(onShift ? "Action_PunchOut" : "Action_PunchIn")}.");
        CharacterAction action = onShift
            ? (CharacterAction)new Action_PunchOut(worker, _building)
            : new Action_PunchIn(worker, _building);

        var actions = worker.CharacterActions;
        if (actions == null)
        {
            Debug.LogError($"<color=red>[TimeClock:RunCycle]</color> aborted — worker.CharacterActions is NULL on '{worker.CharacterName}'.");
            Furniture.Release();
            return;
        }
        bool executed = actions.ExecuteAction(action);
        Debug.Log($"<color=magenta>[TimeClock:RunCycle]</color> CharacterActions.ExecuteAction returned {executed} (CurrentAction was '{(actions.CurrentAction != null ? actions.CurrentAction.GetType().Name : "<null>")}' before call).");
        if (!executed)
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
