using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Single HUD anchor for all speech bubbles on the local player's screen.
/// Bubbles from every character's SpeechBubbleStack parent under ContentRoot and
/// position themselves via Camera.WorldToScreenPoint each frame.
///
/// Lifecycle: lives on the local player's HUD Canvas prefab. OnEnable registers
/// itself as HUDSpeechBubbleLayer.Local; OnDisable clears the static. Camera and
/// LocalPlayerAnchor are resolved lazily and re-resolved whenever the cached
/// reference becomes null (portal-gate return, character respawn, camera rebind).
/// </summary>
public class HUDSpeechBubbleLayer : MonoBehaviour
{
    public static HUDSpeechBubbleLayer Local { get; private set; }

    [SerializeField] private RectTransform _contentRoot;
    [Tooltip("Optional explicit camera override. If null, resolves Camera.main lazily.")]
    [SerializeField] private Camera _cameraOverride;

    private Camera _cachedCamera;
    private Transform _cachedLocalPlayerAnchor;

    public RectTransform ContentRoot => _contentRoot;

    public Camera Camera
    {
        get
        {
            if (_cameraOverride != null) return _cameraOverride;
            if (_cachedCamera == null) _cachedCamera = Camera.main;
            return _cachedCamera;
        }
    }

    /// <summary>
    /// Resolves the local player's speech anchor transform. Returns null while no
    /// local player Character exists yet (session boot, scene transition).
    /// </summary>
    public Transform LocalPlayerAnchor
    {
        get
        {
            if (_cachedLocalPlayerAnchor != null) return _cachedLocalPlayerAnchor;
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm == null || nm.LocalClient == null) return null;
                var playerObj = nm.LocalClient.PlayerObject;
                if (playerObj == null) return null;
                var character = playerObj.GetComponent<Character>();
                if (character == null) return null;
                _cachedLocalPlayerAnchor = character.transform;
                return _cachedLocalPlayerAnchor;
            }
            catch (Exception e)
            {
                Debug.LogError($"[HUDSpeechBubbleLayer] LocalPlayerAnchor resolve failed: {e.Message}");
                return null;
            }
        }
    }

    private void OnEnable()
    {
        if (Local != null && Local != this)
        {
            Debug.LogWarning($"[HUDSpeechBubbleLayer] A second instance enabled on '{gameObject.name}'. Replacing previous.");
        }
        Local = this;
    }

    private void OnDisable()
    {
        if (Local == this) Local = null;
        _cachedCamera = null;
        _cachedLocalPlayerAnchor = null;
    }
}
