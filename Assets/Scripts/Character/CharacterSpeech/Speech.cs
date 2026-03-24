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
        try 
        {
            _character = owner;
            if (_typeRoutine != null) StopCoroutine(_typeRoutine);
            _typeRoutine = StartCoroutine(TypeMessage(message, source, voice, characterPitch, typingSpeed, onComplete));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"<color=red>[Speech CRASH]</color> Exception in Setup: {e.Message}\n{e.StackTrace}");
        }
    }

    private IEnumerator TypeMessage(string message, AudioSource source, VoiceSO voice, float characterPitch, float typingSpeed, System.Action onComplete)
    {
        _textElement.text = "";
        int charCount = 0;

        // Utilisation du paramètre s'il est > 0, sinon utilisation de la variable sérialisée
        float currentSpeed = typingSpeed > 0f ? typingSpeed : _typingSpeed;

        if (currentSpeed <= 0f)
        {
            // Typage instantané
            _textElement.text = message;
            _typeRoutine = null;
            onComplete?.Invoke();
            yield break;
        }

        float timeAccumulator = 0f;
        char[] characters = message.ToCharArray();

        while (charCount < characters.Length)
        {
            // Accumulateur de temps basé sur Time.unscaledDeltaTime pour la lisibilité
            // Ce système permet d'afficher plusieurs lettres par frame si la vitesse est très rapide !
            timeAccumulator += Time.unscaledDeltaTime;

            int lettersToAdd = Mathf.FloorToInt(timeAccumulator / currentSpeed);

            if (lettersToAdd > 0)
            {
                int lettersAdded = 0;
                while (lettersAdded < lettersToAdd && charCount < characters.Length)
                {
                    char letter = characters[charCount];
                    _textElement.text += letter;
                    charCount++;
                    lettersAdded++;
                    
                    if (letter != ' ' && charCount % 3 == 0 && voice != null && source != null)
                    {
                        AudioClip clipToPlay = voice.GetRandomClip();
                        if (clipToPlay != null)
                        {
                            source.pitch = characterPitch + Random.Range(-0.05f, 0.05f);
                            source.PlayOneShot(clipToPlay);
                        }
                    }
                }
                
                // Soustraire le temps consommé
                timeAccumulator -= lettersToAdd * currentSpeed;
            }

            yield return null; // Attendre la frame suivante
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