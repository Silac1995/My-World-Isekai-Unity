using UnityEngine;
using System;

public class ScriptedSpeech : Speech
{
    private Action _currentOnComplete;

    public void SetupScripted(Character owner, string message, AudioSource source, VoiceSO voice, float characterPitch, float typingSpeed = 0f, Action onComplete = null)
    {
        _currentOnComplete = onComplete;

        // We use the base Setup but pass a specific callback for when typing finishes
        base.Setup(owner, message, source, voice, characterPitch, typingSpeed, () => {
            _currentOnComplete?.Invoke();
            _currentOnComplete = null;
        });
    }

    public void Clear()
    {
        _currentOnComplete = null;
        gameObject.SetActive(false);
    }
}
