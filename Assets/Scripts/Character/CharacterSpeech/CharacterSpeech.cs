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

    private Coroutine _hideCoroutine;
    private Coroutine _testCoroutine;

    private readonly string[] _randomPhrases = {
        "Tiens, un bug ? Ah non, c'est une feature.",
        "Je me sens... observé par un script.",
        "Vivement le mode multijoueur !",
        "Est-ce que mes sprites sont bien alignés ?",
        "42 est la réponse, mais quelle était la question ?"
    };

    private void Start()
    {
        if (_speechBubblePrefab != null) _speechBubblePrefab.SetActive(false);
        _testCoroutine = StartCoroutine(RandomTalkRoutine());
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

            speechScript.Setup(_character, message, _audioSource, _voiceSO, () => {
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