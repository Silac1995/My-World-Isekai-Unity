using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Singleton HUD layer that spawns one <see cref="UI_RemoteActionIndicator"/>
/// per remote Character (every Character except the local player) and toggles
/// visibility based on a PlayerPrefs-backed setting.
///
/// Lifecycle:
/// - <see cref="Local"/> registers in <c>OnEnable</c>, clears in <c>OnDisable</c>.
/// - Subscribes to <c>Character.OnCharacterSpawned</c> + <c>OnCharacterDespawned</c>
///   so new arrivals get an indicator immediately and disconnects clean up.
/// - <see cref="SetEnabled"/> flips the runtime + persists to PlayerPrefs.
/// - Default off. The setting is keyed by <see cref="PlayerPrefsKey"/>.
///
/// The local player is intentionally skipped — the labeled
/// <c>UI_Action_ProgressBar</c> serves the local player; this layer is for
/// ambient awareness of others.
/// </summary>
public class RemoteActionIndicatorLayer : MonoBehaviour
{
    public const string PlayerPrefsKey = "MWI.HUD.ShowRemoteActionBars";

    public static RemoteActionIndicatorLayer Local { get; private set; }

    [Header("Wiring")]
    [Tooltip("Parent RectTransform for spawned indicators. Typically the HUD canvas root.")]
    [SerializeField] private RectTransform _contentRoot;

    [Tooltip("Leaf prefab — must have a UI_RemoteActionIndicator component on the root.")]
    [SerializeField] private UI_RemoteActionIndicator _indicatorPrefab;

    [Header("Defaults")]
    [Tooltip("Initial enabled state when no PlayerPrefs value has been saved yet.")]
    [SerializeField] private bool _defaultEnabled = false;

    private readonly Dictionary<Character, UI_RemoteActionIndicator> _indicators = new();
    private Transform _cachedLocalPlayerAnchor;
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;
    public RectTransform ContentRoot => _contentRoot;

    private void OnEnable()
    {
        if (Local != null && Local != this)
            Debug.LogWarning($"[RemoteActionIndicatorLayer] A second instance enabled on '{gameObject.name}'. Replacing previous.");
        Local = this;

        _isEnabled = ReadEnabledPref();

        Character.OnCharacterSpawned += HandleCharacterSpawned;
        Character.OnCharacterDespawned += HandleCharacterDespawned;

        // Backfill: characters that spawned BEFORE this layer existed (host-side
        // boot, or a freshly-joined client receiving the world snapshot) won't
        // re-fire OnCharacterSpawned. Scan the scene once.
        BackfillExistingCharacters();
    }

    private void OnDisable()
    {
        Character.OnCharacterSpawned -= HandleCharacterSpawned;
        Character.OnCharacterDespawned -= HandleCharacterDespawned;

        ClearAll();

        if (Local == this) Local = null;
        _cachedLocalPlayerAnchor = null;
    }

    /// <summary>
    /// Public setter for runtime toggle (chat command, dev panel, future settings UI).
    /// Persists to PlayerPrefs.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (_isEnabled == enabled) return;
        _isEnabled = enabled;
        PlayerPrefs.SetInt(PlayerPrefsKey, enabled ? 1 : 0);
        PlayerPrefs.Save();

        if (_isEnabled)
            BackfillExistingCharacters();
        else
            ClearAll();
    }

    public void Toggle() => SetEnabled(!_isEnabled);

    private bool ReadEnabledPref()
    {
        if (!PlayerPrefs.HasKey(PlayerPrefsKey)) return _defaultEnabled;
        return PlayerPrefs.GetInt(PlayerPrefsKey, _defaultEnabled ? 1 : 0) != 0;
    }

    private void HandleCharacterSpawned(Character c)
    {
        if (!_isEnabled || c == null) return;
        if (IsLocalPlayer(c)) return;
        SpawnIndicatorFor(c);
    }

    private void HandleCharacterDespawned(Character c)
    {
        if (c == null) return;
        if (_indicators.TryGetValue(c, out var ind) && ind != null)
        {
            ind.Unbind();
            Destroy(ind.gameObject);
        }
        _indicators.Remove(c);
    }

    private void BackfillExistingCharacters()
    {
        if (!_isEnabled) return;
        // Include inactive — hibernation can leave a Character disabled but valid.
        var all = FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in all)
        {
            if (c == null) continue;
            if (IsLocalPlayer(c)) continue;
            if (_indicators.ContainsKey(c)) continue;
            SpawnIndicatorFor(c);
        }
    }

    private void SpawnIndicatorFor(Character c)
    {
        if (_indicatorPrefab == null)
        {
            Debug.LogWarning("<color=orange>[RemoteActionIndicatorLayer]</color> _indicatorPrefab is null — author the UI_RemoteActionIndicator prefab and wire it in the Inspector.");
            return;
        }
        if (_contentRoot == null)
        {
            Debug.LogWarning("<color=orange>[RemoteActionIndicatorLayer]</color> _contentRoot is null — wire a RectTransform under the HUD canvas in the Inspector.");
            return;
        }
        if (_indicators.ContainsKey(c)) return;

        var ind = Instantiate(_indicatorPrefab, _contentRoot, false);
        ind.gameObject.name = $"UI_RemoteActionIndicator [{c.CharacterName}]";
        ind.Bind(c, _contentRoot, ResolveLocalPlayerAnchor());
        _indicators[c] = ind;
    }

    private void ClearAll()
    {
        foreach (var kv in _indicators)
        {
            if (kv.Value == null) continue;
            kv.Value.Unbind();
            Destroy(kv.Value.gameObject);
        }
        _indicators.Clear();
    }

    private bool IsLocalPlayer(Character c)
    {
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return false;
            var localObj = nm.LocalClient.PlayerObject;
            if (localObj == null) return false;
            return c.NetworkObject == localObj;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    private Transform ResolveLocalPlayerAnchor()
    {
        if (_cachedLocalPlayerAnchor != null) return _cachedLocalPlayerAnchor;
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.LocalClient == null) return null;
            var playerObj = nm.LocalClient.PlayerObject;
            if (playerObj == null) return null;
            _cachedLocalPlayerAnchor = playerObj.transform;
            return _cachedLocalPlayerAnchor;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }

    // Late-bound recovery: if indicators got spawned before the local player
    // existed, propagate the anchor once it resolves so distance-fade math
    // starts working.
    private void Update()
    {
        if (_indicators.Count == 0) return;
        if (_cachedLocalPlayerAnchor != null) return;
        var anchor = ResolveLocalPlayerAnchor();
        if (anchor == null) return;
        foreach (var kv in _indicators)
        {
            if (kv.Value != null) kv.Value.SetLocalPlayerAnchor(anchor);
        }
    }
}
