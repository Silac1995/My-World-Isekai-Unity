using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Netcode;
using MWI.Needs;

public class CharacterNeeds : CharacterSystem, ICharacterSaveData<NeedsSaveData>
{
    private List<CharacterNeed> _allNeeds = new List<CharacterNeed>();
    public List<CharacterNeed> AllNeeds => _allNeeds;

    /// <summary>
    /// Typed accessor for needs. Returns null if no need of type T is registered.
    /// </summary>
    public T GetNeed<T>() where T : CharacterNeed
    {
        foreach (var need in _allNeeds)
        {
            if (need is T typed) return typed;
        }
        return null;
    }

    private NeedSocial _socialNeed;

    // ── Server-authoritative hunger ─────────────────────────────────────────
    // The single source of truth for NeedHunger.CurrentValue across all peers.
    // Server writes (phase decay, eating); clients read via OnValueChanged bridge.
    // Starts full (DEFAULT_MAX) so late-joiners see something sensible while
    // spawn handshake replicates the actual value.
    private NetworkVariable<float> _networkedHunger = new NetworkVariable<float>(
        NeedHungerMath.DEFAULT_MAX,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Current hunger value as held in the NetworkVariable. Read-only public surface.</summary>
    public float NetworkedHungerValue => _networkedHunger.Value;

    /// <summary>
    /// Subscribe a handler to the underlying NetworkVariable's OnValueChanged.
    /// Used by NeedHunger to bridge replicated changes into its public Action&lt;float&gt; events.
    /// Pairs with <see cref="UnsubscribeNetworkedHungerChanged"/> for cleanup.
    /// </summary>
    public void SubscribeNetworkedHungerChanged(NetworkVariable<float>.OnValueChangedDelegate handler)
    {
        _networkedHunger.OnValueChanged += handler;
    }

    /// <summary>
    /// Unsubscribe a previously-registered handler from the NetworkVariable.
    /// </summary>
    public void UnsubscribeNetworkedHungerChanged(NetworkVariable<float>.OnValueChangedDelegate handler)
    {
        _networkedHunger.OnValueChanged -= handler;
    }

    /// <summary>
    /// Server-only direct write to the hunger NetworkVariable. Clamps to [0, MaxValue].
    /// Used by NeedHunger for both phase decay and IncreaseValue/DecreaseValue when on the server.
    /// </summary>
    public void ServerSetHunger(float value)
    {
        if (!IsSpawned)
        {
            // Pre-spawn write (e.g., during OnNetworkPreSpawn or save-restore that runs before spawn).
            // NetworkVariable supports server-side writes pre-spawn — clients receive the initial value.
            // No-op guard: if not server, skip.
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        }
        else if (!IsServer)
        {
            Debug.LogWarning($"<color=orange>[CharacterNeeds]</color> ServerSetHunger called on non-server peer for {gameObject.name}. Ignored.");
            return;
        }

        float clamped = Mathf.Clamp(value, 0f, NeedHungerMath.DEFAULT_MAX);
        _networkedHunger.Value = clamped;
    }

    /// <summary>
    /// Client → Server request to bump the hunger value by <paramref name="amount"/> (positive = restore, negative = drain).
    /// Server validates and writes the NetworkVariable. Used by FoodInstance.ApplyEffect when called on a non-server peer.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestAdjustHungerRpc(float amount)
    {
        // Defensive: server is the only writer.
        if (!IsServer) return;

        float current = _networkedHunger.Value;
        ServerSetHunger(current + amount);
    }

    // ── Server-authoritative sleep ──────────────────────────────────────────
    // The single source of truth for NeedSleep.CurrentValue across all peers.
    // Server writes (phase decay, sleep restore); clients read via OnValueChanged bridge.
    private NetworkVariable<float> _networkedSleep = new NetworkVariable<float>(
        NeedSleepMath.DEFAULT_MAX,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public float NetworkedSleepValue => _networkedSleep.Value;

    public void SubscribeNetworkedSleepChanged(NetworkVariable<float>.OnValueChangedDelegate handler)
    {
        _networkedSleep.OnValueChanged += handler;
    }

    public void UnsubscribeNetworkedSleepChanged(NetworkVariable<float>.OnValueChangedDelegate handler)
    {
        _networkedSleep.OnValueChanged -= handler;
    }

    public void ServerSetSleep(float value)
    {
        if (!IsSpawned)
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;
        }
        else if (!IsServer)
        {
            Debug.LogWarning($"<color=orange>[CharacterNeeds]</color> ServerSetSleep called on non-server peer for {gameObject.name}. Ignored.");
            return;
        }

        float clamped = Mathf.Clamp(value, 0f, NeedSleepMath.DEFAULT_MAX);
        _networkedSleep.Value = clamped;
    }

    [Rpc(SendTo.Server)]
    public void RequestAdjustSleepRpc(float amount)
    {
        if (!IsServer) return;
        ServerSetSleep(_networkedSleep.Value + amount);
    }

    protected override void Awake()
    {
        base.Awake();

        // Register all needs in Awake so GetNeed<T>() works inside OnNetworkSpawn,
        // BEFORE PlayerUI.Initialize → UI_HungerBar.Initialize fires. Previously
        // these were created in Start(), which runs AFTER OnNetworkSpawn — that
        // caused the local player's UI_HungerBar to receive null and display 0/0.
        if (_character == null)
        {
            Debug.LogError($"<color=red>[CharacterNeeds]</color> _character is null in Awake on {gameObject.name}. Needs will not be initialised.");
            return;
        }

        if (_allNeeds.Count == 0)
        {
            _socialNeed = new NeedSocial(_character);
            _allNeeds.Add(_socialNeed);

            _allNeeds.Add(new NeedToWearClothing(_character));
            _allNeeds.Add(new NeedJob(_character));

            // NeedHunger needs a back-reference to CharacterNeeds so it can read/write the NetworkVariable.
            var hunger = new NeedHunger(_character, this);
            _allNeeds.Add(hunger);

            var sleep = new NeedSleep(_character, this);
            _allNeeds.Add(sleep);
        }
    }

    protected override void OnNetworkPreSpawn(ref NetworkManager networkManager)
    {
        base.OnNetworkPreSpawn(ref networkManager);

        // Server-side: seed the hunger NetworkVariable to the default starting value (80) so
        // a fresh character spawns at 80/100 instead of the NV constructor default of 100/100.
        // Save-restore (ImportProfile → Deserialize) runs AFTER this and overwrites with the
        // saved value if applicable. Clients receive whichever is the final value via standard
        // NetworkVariable replication.
        if (networkManager != null && networkManager.IsServer)
        {
            _networkedHunger.Value = NeedHunger.DEFAULT_START;
            _networkedSleep.Value = NeedSleep.DEFAULT_START;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Phase-tick subscription: defensive, may run on server or client. NeedHunger
        // itself guards HandlePhaseChanged with IsServer so non-authoritative peers
        // never decay locally.
        var hunger = GetNeed<NeedHunger>();
        if (hunger != null)
        {
            hunger.TrySubscribeToPhase();
            hunger.BindNetworkBridge();
        }

        var sleep = GetNeed<NeedSleep>();
        if (sleep != null)
        {
            sleep.TrySubscribeToPhase();
            sleep.BindNetworkBridge();
        }

        if (IsServer && MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay += HandleNewDay;
        }

        // NeedJob runs an OnNewDay-driven candidate scan on the server. Wire it through the
        // same lifecycle hook as the rest of the needs — TrySubscribeToOnNewDay is idempotent
        // and IsServer-gated internally, so this is safe on every peer.
        var job = GetNeed<NeedJob>();
        if (job != null)
        {
            job.TrySubscribeToOnNewDay();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        var hunger = GetNeed<NeedHunger>();
        if (hunger != null)
        {
            hunger.UnsubscribeFromPhase();
            hunger.UnbindNetworkBridge();
        }

        var sleep = GetNeed<NeedSleep>();
        if (sleep != null)
        {
            sleep.UnsubscribeFromPhase();
            sleep.UnbindNetworkBridge();
        }

        var job = GetNeed<NeedJob>();
        if (job != null)
        {
            job.UnsubscribeFromOnNewDay();
        }

        if (IsServer && MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }
    }

    private void HandleNewDay()
    {
        // Server-only — NeedSocial decay is currently a plain POCO, so only the server
        // should decay it. (Future: migrate NeedSocial to the same NetworkVariable pattern as hunger.)
        if (!IsServer) return;

        if (_socialNeed != null)
        {
            // Decays social need by 15 every in-game day
            _socialNeed.DecreaseValue(45f);
        }
    }

    public override void OnDestroy()
    {
        // Defensive cleanup. OnNetworkDespawn already unsubscribes everything for the
        // happy path, but if the object is destroyed without despawning (editor stop,
        // domain reload, scene unload mid-spawn) we still need to release the listeners.
        var hungerNeed = GetNeed<NeedHunger>();
        if (hungerNeed != null)
        {
            hungerNeed.UnsubscribeFromPhase();
            hungerNeed.UnbindNetworkBridge();
        }

        var sleepNeed = GetNeed<NeedSleep>();
        if (sleepNeed != null)
        {
            sleepNeed.UnsubscribeFromPhase();
            sleepNeed.UnbindNetworkBridge();
        }

        var jobNeed = GetNeed<NeedJob>();
        if (jobNeed != null)
        {
            jobNeed.UnsubscribeFromOnNewDay();
        }

        if (MWI.Time.TimeManager.Instance != null)
        {
            MWI.Time.TimeManager.Instance.OnNewDay -= HandleNewDay;
        }

        base.OnDestroy();
    }

    // --- ICharacterSaveData IMPLEMENTATION ---

    public string SaveKey => "CharacterNeeds";
    public int LoadPriority => 40;

    public NeedsSaveData Serialize()
    {
        var data = new NeedsSaveData();

        foreach (var need in _allNeeds)
        {
            data.needs.Add(new NeedSaveEntry
            {
                needType = need.GetType().Name,
                value = need.CurrentValue
            });
        }

        return data;
    }

    public void Deserialize(NeedsSaveData data)
    {
        if (data == null || data.needs == null) return;

        foreach (var entry in data.needs)
        {
            var matchingNeed = _allNeeds.Find(n => n.GetType().Name == entry.needType);
            if (matchingNeed != null)
            {
                // For NeedHunger this writes through to the NetworkVariable when called
                // on the server (the only place save-restore runs). Clients receive the
                // value via standard NetworkVariable replication.
                matchingNeed.CurrentValue = entry.value;
            }
            else
            {
                Debug.LogWarning($"<color=yellow>[CharacterNeeds]</color> No matching need found for saved type '{entry.needType}' on {_character.CharacterName}.");
            }
        }
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
