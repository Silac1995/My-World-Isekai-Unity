using UnityEngine;
using System.Collections;

public class CharacterSpeech : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private GameObject _speechBubblePrefab; // Ton objet déjà présent dans la hiérarchie

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

        // Arrêt du timer de disparition précédent si on parle à nouveau
        if (_hideCoroutine != null)
        {
            StopCoroutine(_hideCoroutine);
        }

        // Mise à jour du texte via le script Speech attaché au prefab
        if (_speechBubblePrefab.TryGetComponent<Speech>(out var speechScript))
        {
            speechScript.Setup(_character, message);
        }

        // Activation visuelle
        _speechBubblePrefab.SetActive(true);

        // Lancement du nouveau timer
        _hideCoroutine = StartCoroutine(HideSpeechAfterDelay(duration));
    }

    private IEnumerator HideSpeechAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_speechBubblePrefab != null)
        {
            _speechBubblePrefab.SetActive(false);
        }

        _hideCoroutine = null;
    }
}