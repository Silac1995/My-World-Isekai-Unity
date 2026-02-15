using UnityEngine;
using TMPro;
using System.Collections;
using System;

public class Speech : MonoBehaviour
{
    private Character _character;
    [SerializeField] private TextMeshProUGUI _textElement;
    [SerializeField] private float _typingSpeed = 0.04f; // Vitesse de défilement

    private Coroutine _typeRoutine;

    public void Setup(Character owner, string message, Action onComplete = null)
    {
        _character = owner;

        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = StartCoroutine(TypeMessage(message, onComplete));
    }

    private IEnumerator TypeMessage(string message, Action onComplete)
    {
        _textElement.text = "";

        foreach (char letter in message.ToCharArray())
        {
            _textElement.text += letter;
            yield return new WaitForSeconds(_typingSpeed);
        }

        _typeRoutine = null;
        onComplete?.Invoke(); // On prévient que le texte est fini
    }

    private void OnDisable()
    {
        if (_typeRoutine != null) StopCoroutine(_typeRoutine);
        _typeRoutine = null;
    }
}