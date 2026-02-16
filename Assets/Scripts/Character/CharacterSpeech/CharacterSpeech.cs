using UnityEngine;
using System.Collections;
using Random = UnityEngine.Random; // <-- Ajoute ça ici

public class CharacterSpeech : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private GameObject _speechBubblePrefab;
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;

    [Header("Voice Settings")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private VoiceSO _voiceSO;
    [Range(0.85f, 1.25f)][SerializeField] private float _voicePitch = 1.0f;

    private Coroutine _hideCoroutine;
    private Coroutine _testCoroutine;

    private readonly string[] _randomPhrases = {
        "Battle System, WHEN!?.",
        "I can't wait to get a home.",
        "42 is the answer, but what was the question?",
        "I wish I had hair...",
        "Je suis français, oui oui."
    };

    private void Start()
    {
        // On s'assure que la bulle est éteinte
        if (_speechBubblePrefab != null) _speechBubblePrefab.SetActive(false);

        // Randomisation du pitch pour l'identité unique du NPC
        // On varie autour de la valeur actuelle pour garder une base si tu l'as réglée
        _voicePitch = Random.Range(0.85f, 1.25f);

        // Lancement de la routine de parole automatique
        _testCoroutine = StartCoroutine(RandomTalkRoutine());

        Debug.Log($"<color=cyan>[Speech]</color> {gameObject.name} initialized with pitch: {_voicePitch:F2}");
    }

    private void OnDisable()
    {
        if (_testCoroutine != null) StopCoroutine(_testCoroutine);
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _testCoroutine = null;
        _hideCoroutine = null;
    }

    private IEnumerator RandomTalkRoutine()
    {
        while (true)
        {
            // On génère un délai aléatoire entre 3 et 10 secondes
            float randomWait = Random.Range(3f, 10f);
            yield return new WaitForSeconds(randomWait);

            if (_randomPhrases.Length > 0)
            {
                string randomText = _randomPhrases[Random.Range(0, _randomPhrases.Length)];
                Say(randomText);
            }
        }
    }

    public void Say(string message, float duration = 3f)
    {
        if (_speechBubblePrefab == null) return;

        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);

        _bodyPartsController?.MouthController?.StartTalking();

        if (_speechBubblePrefab.TryGetComponent<Speech>(out var speechScript))
        {
            _speechBubblePrefab.SetActive(true);

            // On passe maintenant _voicePitch au Setup de la bulle
            speechScript.Setup(_character, message, _audioSource, _voiceSO, _voicePitch, () => {
                _bodyPartsController?.MouthController?.StopTalking();
                _hideCoroutine = StartCoroutine(HideSpeechAfterDelay(duration));
            });
        }
    }

    private IEnumerator HideSpeechAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_speechBubblePrefab != null) _speechBubblePrefab.SetActive(false);
        _hideCoroutine = null;
    }
}