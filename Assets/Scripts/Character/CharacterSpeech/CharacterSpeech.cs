using UnityEngine;
using System.Collections;
using System;
using Random = UnityEngine.Random;

public class CharacterSpeech : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private GameObject _speechBubblePrefab;
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;

    [Header("Voice Settings")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private VoiceSO _voiceSO;
    [Range(0.85f, 1.25f)][SerializeField] private float _voicePitch = 1.0f;

    [SerializeField] private ScriptedSpeech _scriptedSpeech;
    
    private Coroutine _hideCoroutine;
    public bool IsSpeaking => _hideCoroutine != null;

    private void Start()
    {
        if (_speechBubblePrefab != null) _speechBubblePrefab.SetActive(false);
        _voicePitch = Random.Range(0.85f, 1.25f);
        Debug.Log($"<color=cyan>[Speech]</color> {gameObject.name} initialized with pitch: {_voicePitch:F2}");
    }

    private void OnDisable()
    {
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _hideCoroutine = null;
    }

    public void Say(string message, float duration = 3f, float typingSpeed = 0f)
    {
        if (_speechBubblePrefab == null) return;
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _bodyPartsController?.MouthController?.StartTalking();
        if (_speechBubblePrefab.TryGetComponent<Speech>(out var speechScript))
        {
            _speechBubblePrefab.SetActive(true);
            speechScript.Setup(_character, message, _audioSource, _voiceSO, _voicePitch, typingSpeed, () => {
                _bodyPartsController?.MouthController?.StopTalking();
                _hideCoroutine = StartCoroutine(HideSpeechAfterDelay(duration));
            });
        }
    }

    public void SayScripted(string message, float typingSpeed = 0f, Action onTypingFinished = null)
    {
        if (_speechBubblePrefab == null) return;
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        
        _bodyPartsController?.MouthController?.StartTalking();
        
        // Use ScriptedSpeech if available, otherwise fallback to standard Setup but without HideAfterDelay
        if (_scriptedSpeech != null)
        {
            _speechBubblePrefab.SetActive(true);
            _scriptedSpeech.OnTypingFinished += () => {
                _bodyPartsController?.MouthController?.StopTalking();
                onTypingFinished?.Invoke();
            };
            _scriptedSpeech.SetupScripted(_character, message, _audioSource, _voiceSO, _voicePitch, typingSpeed);
        }
        else if (_speechBubblePrefab.TryGetComponent<Speech>(out var speechScript))
        {
            _speechBubblePrefab.SetActive(true);
            speechScript.Setup(_character, message, _audioSource, _voiceSO, _voicePitch, typingSpeed, () => {
                _bodyPartsController?.MouthController?.StopTalking();
                onTypingFinished?.Invoke();
            });
        }
    }

    public void CloseSpeech()
    {
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _hideCoroutine = null;
        if (_speechBubblePrefab != null) _speechBubblePrefab.SetActive(false);
        _bodyPartsController?.MouthController?.StopTalking();
    }

    private IEnumerator HideSpeechAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_speechBubblePrefab != null) _speechBubblePrefab.SetActive(false);
        _hideCoroutine = null;
    }
}
