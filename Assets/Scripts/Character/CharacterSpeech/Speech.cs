using UnityEngine;
using TMPro;
using System.Collections;
using System;
using Random = UnityEngine.Random;

public class Speech : MonoBehaviour
{
    private Character _character;
    [SerializeField] private TextMeshProUGUI _textElement;
    [SerializeField] private float _typingSpeed = 0.06f; // Vitesse de d�filement

    private Coroutine _typeRoutine;

    public void Setup(Character owner, string message, AudioSource source, VoiceSO voice, float characterPitch, System.Action onComplete = null)
    {
        _character = owner;
        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = StartCoroutine(TypeMessage(message, source, voice, characterPitch, onComplete));
    }

    private IEnumerator TypeMessage(string message, AudioSource source, VoiceSO voice, float characterPitch, System.Action onComplete)
    {
        _textElement.text = "";
        int charCount = 0;

        foreach (char letter in message.ToCharArray())
        {
            _textElement.text += letter;
            charCount++;

            if (letter != ' ' && charCount % 3 == 0 && voice != null && source != null)
            {
                AudioClip clipToPlay = voice.GetRandomClip();
                if (clipToPlay != null)
                {
                    // On applique le pitch unique du personnage
                    // Optionnel : on ajoute un tout petit offset pour le r�alisme
                    source.pitch = characterPitch + Random.Range(-0.05f, 0.05f);
                    source.PlayOneShot(clipToPlay);
                }
            }
            yield return new WaitForSeconds(_typingSpeed);
        }
        _typeRoutine = null;
        onComplete?.Invoke();
    }

    private void OnDisable()
    {
        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = null;
    }
}