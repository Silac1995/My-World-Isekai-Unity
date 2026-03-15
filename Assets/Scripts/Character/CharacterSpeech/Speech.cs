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
    public bool IsTyping => _typeRoutine != null;

    public void Setup(Character owner, string message, AudioSource source, VoiceSO voice, float characterPitch, float typingSpeed = 0f, System.Action onComplete = null)
    {
        _character = owner;
        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = StartCoroutine(TypeMessage(message, source, voice, characterPitch, typingSpeed, onComplete));
    }

    private IEnumerator TypeMessage(string message, AudioSource source, VoiceSO voice, float characterPitch, float typingSpeed, System.Action onComplete)
    {
        _textElement.text = "";
        int charCount = 0;

        // Vitesse personnalisée ou aléatoire (0.04 = Rapide, 0.06 = Plus lent)
        float currentSpeed = typingSpeed > 0 ? typingSpeed : Random.Range(0.01f, 0.03f);

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
            yield return new WaitForSeconds(currentSpeed);
        }
        _typeRoutine = null;
        onComplete?.Invoke();
    }

    private void OnDisable()
    {
        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = null;
        if (_textElement != null) _textElement.text = "";
    }
}