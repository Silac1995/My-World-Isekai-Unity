using System;
using System.Collections.Generic;
using MWI.Needs;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative sleep need. Passive (Option A from the design):
/// decays/restores but does NOT drive GOAP. The actual current value lives in
/// <c>CharacterNeeds._networkedSleep</c> (a <see cref="NetworkVariable{T}"/> of
/// type float, server-write / everyone-read). NeedSleep is a thin wrapper that:
/// <list type="bullet">
/// <item>Reads the network value through <c>CharacterNeeds.NetworkedSleepValue</c>.</item>
/// <item>Routes writes through the server (direct NV write if server, else ServerRpc).</item>
/// <item>Bridges <c>NetworkVariable.OnValueChanged</c> to public events
///       (<see cref="OnValueChanged"/>, <see cref="OnExhaustedChanged"/>).</item>
/// <item>Decays once per TimeManager phase on the server.</item>
/// </list>
/// </summary>
public class NeedSleep : CharacterNeed
{
    public const float DEFAULT_START = NeedSleepMath.DEFAULT_START;

    private readonly CharacterNeeds _owner;

    private bool _phaseSubscribed;
    private bool _bridgeBound;
    private bool _isExhausted;

    public event Action<float> OnValueChanged;
    public event Action<bool> OnExhaustedChanged;

    public float MaxValue => NeedSleepMath.DEFAULT_MAX;
    public bool IsExhausted => _isExhausted;

    public override float CurrentValue
    {
        get
        {
            if (_owner == null) return 0f;
            return _owner.NetworkedSleepValue;
        }
        set
        {
            if (_owner == null) return;

            float current = _owner.NetworkedSleepValue;
            float clampedTarget = Mathf.Clamp(value, 0f, MaxValue);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                _owner.ServerSetSleep(clampedTarget);
            }
            else
            {
                float delta = clampedTarget - current;
                if (Mathf.Approximately(delta, 0f)) return;
                _owner.RequestAdjustSleepRpc(delta);
            }
        }
    }

    public NeedSleep(Character character, CharacterNeeds owner) : base(character)
    {
        _owner = owner;
    }

    public void IncreaseValue(float amount)
    {
        if (_owner == null || Mathf.Approximately(amount, 0f)) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            _owner.ServerSetSleep(_owner.NetworkedSleepValue + amount);
        else
            _owner.RequestAdjustSleepRpc(amount);
    }

    public void DecreaseValue(float amount)
    {
        if (_owner == null || Mathf.Approximately(amount, 0f)) return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            _owner.ServerSetSleep(_owner.NetworkedSleepValue - amount);
        else
            _owner.RequestAdjustSleepRpc(-amount);
    }

    public bool IsLow() => CurrentValue <= NeedSleepMath.DEFAULT_LOW_THRESHOLD;

    public void TrySubscribeToPhase()
    {
        if (_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance == null) return;
        MWI.Time.TimeManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        _phaseSubscribed = true;
    }

    public void UnsubscribeFromPhase()
    {
        if (!_phaseSubscribed) return;
        if (MWI.Time.TimeManager.Instance != null)
            MWI.Time.TimeManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        _phaseSubscribed = false;
    }

    public void BindNetworkBridge()
    {
        if (_bridgeBound || _owner == null) return;
        _owner.SubscribeNetworkedSleepChanged(HandleNetworkedSleepChanged);
        _bridgeBound = true;

        float current = _owner.NetworkedSleepValue;
        _isExhausted = current <= 0f;

        try { OnValueChanged?.Invoke(current); }
        catch (Exception e) { Debug.LogException(e); }

        if (_isExhausted)
        {
            try { OnExhaustedChanged?.Invoke(true); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    public void UnbindNetworkBridge()
    {
        if (!_bridgeBound || _owner == null) return;
        _owner.UnsubscribeNetworkedSleepChanged(HandleNetworkedSleepChanged);
        _bridgeBound = false;
    }

    private void HandleNetworkedSleepChanged(float previous, float current)
    {
        try { OnValueChanged?.Invoke(current); }
        catch (Exception e) { Debug.LogException(e); }

        bool nowExhausted = current <= 0f;
        if (nowExhausted != _isExhausted)
        {
            _isExhausted = nowExhausted;
            try { OnExhaustedChanged?.Invoke(_isExhausted); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    private void HandlePhaseChanged(MWI.Time.DayPhase _)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (_character == null) return;

        // Don't decay while the character is sleeping — they're literally restoring,
        // not depleting. This avoids "wake up, see sleep meter dropped during the
        // phase tick that fired mid-skip" weirdness.
        if (_character.IsSleeping) return;

        try
        {
            DecreaseValue(NeedSleepMath.DEFAULT_DECAY_PER_PHASE);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    // Passive — no GOAP integration.
    public override bool IsActive() => false;
    public override float GetUrgency() => 0f;
    public override GoapGoal GetGoapGoal() => null;
    public override List<GoapAction> GetGoapActions() => new List<GoapAction>();
}
