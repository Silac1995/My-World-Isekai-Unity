using UnityEngine;
using System.Collections;

public class CharacterSpeech : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private GameObject _speechBubblePrefab; // Ton objet déjà présent dans la hiérarchie
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;

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
        // On s'assure qu'il est éteint au début
        if (_speechBubblePrefab != null) _speechBubblePrefab.SetActive(false);

        // Lancement du test de 5 secondes
        _testCoroutine = StartCoroutine(RandomTalkRoutine());
    }

    private void OnDisable()
    {
        // Nettoyage rigoureux des coroutines
        if (_testCoroutine != null) StopCoroutine(_testCoroutine);
        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _testCoroutine = null;
        _hideCoroutine = null;
    }

    private IEnumerator RandomTalkRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(5f);
            string randomText = _randomPhrases[Random.Range(0, _randomPhrases.Length)];
            Say(randomText);
        }
    }

    public void Say(string message, float duration = 3f)
    {
        if (_speechBubblePrefab == null) return;

        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);

        // 1. On active la bouche
        _bodyPartsController?.MouthController?.StartTalking();

        // 2. On lance le texte défilant
        if (_speechBubblePrefab.TryGetComponent<Speech>(out var speechScript))
        {
            _speechBubblePrefab.SetActive(true);

            // On passe une Action (Callback) qui s'exécute quand le texte est fini
            speechScript.Setup(_character, message, () => {
                // Le texte est fini : on arrête la bouche
                _bodyPartsController?.MouthController?.StopTalking();

                // Et on lance le chrono pour faire disparaître la bulle
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