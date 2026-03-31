using UnityEngine;
using System.Collections;
using System;
using Unity.Netcode;
using Random = UnityEngine.Random;

public class CharacterSpeech : CharacterSystem
{
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;
    [SerializeField] private SpeechBubbleStack _speechBubbleStack;

    [Header("Voice Settings")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private VoiceSO _voiceSO;
    [Range(0.85f, 1.25f)][SerializeField] private float _voicePitch = 1.0f;

    private bool _isResetting;

    public bool IsTyping
    {
        get
        {
            return _speechBubbleStack != null && _speechBubbleStack.IsAnyTyping;
        }
    }

    public bool IsSpeaking
    {
        get
        {
            return _speechBubbleStack != null && _speechBubbleStack.HasActiveBubbles;
        }
    }

    private void Start()
    {
        _voicePitch = Random.Range(0.85f, 1.25f);
        Debug.Log($"<color=cyan>[Speech]</color> {gameObject.name} initialized with pitch: {_voicePitch:F2}");
        _speechBubbleStack?.Init(_character.transform, _bodyPartsController?.MouthController);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
    }

    public void Say(string message, float duration = 3f, float typingSpeed = 0f)
    {
        Debug.Log($"<color=cyan>[Speech]</color> {gameObject.name} Say() called. IsServer: {IsServer}, IsClient: {IsClient}, IsOwner: {IsOwner}. Message: {message}");
        if (IsServer)
        {
            Debug.Log($"<color=cyan>[Speech]</color> {gameObject.name} sending SayClientRpc to NotServer...");
            SayClientRpc(message, duration, typingSpeed);
            ExecuteSayLocally(message, duration, typingSpeed);
        }
        else if (IsOwner)
        {
            Debug.Log($"<color=cyan>[Speech]</color> {gameObject.name} sending SayServerRpc as Owner...");
            SayServerRpc(message, duration, typingSpeed);
        }
        else
        {
            Debug.LogWarning($"<color=orange>[Speech]</color> {gameObject.name} Say() called by non-Owner Client. Forcing ExecuteSayLocally (not synced).");
            ExecuteSayLocally(message, duration, typingSpeed);
        }
    }

    [Rpc(SendTo.Server)]
    private void SayServerRpc(string message, float duration, float typingSpeed)
    {
        SayClientRpc(message, duration, typingSpeed);
        ExecuteSayLocally(message, duration, typingSpeed);
    }

    [Rpc(SendTo.NotServer)]
    private void SayClientRpc(string message, float duration, float typingSpeed)
    {
        Debug.Log($"<color=cyan>[Speech] CLIENT RPC RECEIVED</color> {gameObject.name} received SayClientRpc! IsServer: {IsServer}, IsClient: {IsClient}");
        ExecuteSayLocally(message, duration, typingSpeed);
    }

    private void ExecuteSayLocally(string message, float duration, float typingSpeed)
    {
        try
        {
            if (_speechBubbleStack == null)
            {
                Debug.LogError($"[Speech] {gameObject.name} ExecuteSayLocally FAILED: _speechBubbleStack is NULL!");
                return;
            }
            _speechBubbleStack.PushBubble(message, duration, typingSpeed, _audioSource, _voiceSO, _voicePitch);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Speech CRASH] Exception in ExecuteSayLocally: {e.Message}\n{e.StackTrace}");
        }
    }

    public void SayScripted(string message, float typingSpeed = 0f, Action onTypingFinished = null)
    {
        if (IsServer)
        {
            SayScriptedClientRpc(message, typingSpeed);
            ExecuteSayScriptedLocally(message, typingSpeed, onTypingFinished);
        }
        else if (IsOwner)
        {
            SayScriptedServerRpc(message, typingSpeed);
            ExecuteSayScriptedLocally(message, typingSpeed, onTypingFinished);
        }
        else
        {
            ExecuteSayScriptedLocally(message, typingSpeed, onTypingFinished);
        }
    }

    [Rpc(SendTo.Server)]
    private void SayScriptedServerRpc(string message, float typingSpeed)
    {
        SayScriptedClientRpc(message, typingSpeed);
        ExecuteSayScriptedLocally(message, typingSpeed, null);
    }

    [Rpc(SendTo.NotServer)]
    private void SayScriptedClientRpc(string message, float typingSpeed)
    {
        if (IsOwner && !IsServer) return; // Prevent double execution if Client Owner already ran it locally
        ExecuteSayScriptedLocally(message, typingSpeed, null); // Callbacks not supported over net
    }

    private void ExecuteSayScriptedLocally(string message, float typingSpeed, Action onTypingFinished)
    {
        if (_speechBubbleStack == null) return;
        _speechBubbleStack.PushScriptedBubble(message, typingSpeed, _audioSource, _voiceSO, _voicePitch, onTypingFinished);
    }

    public void CloseSpeech()
    {
        if (IsServer)
        {
            CloseSpeechClientRpc();
            ExecuteCloseSpeechLocally();
        }
        else if (IsOwner)
        {
            CloseSpeechServerRpc();
            ExecuteCloseSpeechLocally();
        }
        else
        {
            ExecuteCloseSpeechLocally();
        }
    }

    [Rpc(SendTo.Server)]
    private void CloseSpeechServerRpc()
    {
        CloseSpeechClientRpc();
        ExecuteCloseSpeechLocally();
    }

    [Rpc(SendTo.NotServer)]
    private void CloseSpeechClientRpc()
    {
        if (IsOwner && !IsServer) return; // Client Owner already executed locally
        ExecuteCloseSpeechLocally();
    }

    private void ExecuteCloseSpeechLocally()
    {
        if (_isResetting) _speechBubbleStack?.ClearAll();
        else _speechBubbleStack?.DismissBottom();
    }

    /// <summary>
    /// Formally resets the speech system state, ensuring no scripted logic remains.
    /// </summary>
    public void ResetSpeech()
    {
        _isResetting = true;
        CloseSpeech();
        _isResetting = false;
    }

    protected override void HandleDeath(Character character) => _speechBubbleStack?.ClearAll();
    protected override void HandleIncapacitated(Character character) => _speechBubbleStack?.ClearAll();
}
