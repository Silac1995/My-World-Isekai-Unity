using UnityEngine;
using TMPro;
using System.Collections;
using System;
using Random = UnityEngine.Random;

public class Speech : MonoBehaviour
{
    private Character _character;
    [SerializeField] private TextMeshProUGUI _textElement;
    [SerializeField] private float _typingSpeed = 0.04f; // Vitesse de défilement

    private Coroutine _typeRoutine;

    public void Setup(Character owner, string message, AudioSource source, VoiceSO voice, System.Action onComplete = null)
    {
        _character = owner;

        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        // On lance la coroutine en lui passant la source et le SO
        _typeRoutine = StartCoroutine(TypeMessage(message, source, voice, onComplete));
    }

    private IEnumerator TypeMessage(string message, AudioSource source, VoiceSO voice, System.Action onComplete)
    {
        _textElement.text = "";
        int charCount = 0; // Compteur de caractères affichés
        int soundFrequency = 2; // On joue un son tous les 3 caractères

        foreach (char letter in message.ToCharArray())
        {
            _textElement.text += letter;
            charCount++;

            // Logique audio :
            // 1. Ce n'est pas un espace
            // 2. Le modulo du compteur correspond à notre fréquence
            if (letter != ' ' && charCount % soundFrequency == 0)
            {
                if (voice != null && source != null)
                {
                    AudioClip clipToPlay = voice.GetRandomClip();
                    if (clipToPlay != null)
                    {
                        source.pitch = Random.Range(voice.MinPitch, voice.MaxPitch);
                        source.PlayOneShot(clipToPlay);
                    }
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