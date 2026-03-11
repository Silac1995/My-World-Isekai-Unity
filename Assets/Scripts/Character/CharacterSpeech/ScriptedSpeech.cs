using UnityEngine;
using System;

public class ScriptedSpeech : Speech
{
    public event Action OnTypingFinished;

    public void SetupScripted(Character owner, string message, AudioSource source, VoiceSO voice, float characterPitch, float typingSpeed = 0f)
    {
        // We use the base Setup but pass a specific callback for when typing finishes
        base.Setup(owner, message, source, voice, characterPitch, typingSpeed, () => {
            OnTypingFinished?.Invoke();
        });
    }

    public void Clear()
    {
        gameObject.SetActive(false);
    }
}
