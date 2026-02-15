using System.Collections;
using UnityEngine;

public class MouthController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;
    [SerializeField] private CharacterMouth _characterMouth;

    [Header("Animation Settings")]
    [SerializeField] private float _talkingSpeed = 0.12f;
    private Coroutine _talkRoutine;

    public void Initialize()
    {
        RetrieveMouthObject();
    }

    private void RetrieveMouthObject()
    {
        Transform spritesContainer = (transform.childCount > 0) ? transform.GetChild(0) : transform;
        SpriteRenderer[] allRenderers = spritesContainer.GetComponentsInChildren<SpriteRenderer>(true);

        foreach (var renderer in allRenderers)
        {
            if (renderer.gameObject.name.ToLower().Contains("mouth"))
            {
                _characterMouth = new CharacterMouth(_bodyPartsController, renderer.gameObject);
                SetToNormal(); // État par défaut
                break;
            }
        }
    }

    // --- Méthodes d'expressions fixes ---

    public void SetToNormal() => _characterMouth?.SetSprite("Mouth_normal");

    public void SetToSmile() => _characterMouth?.SetSprite("Mouth_smile");

    public void SetToTalk01() => _characterMouth?.SetSprite("Mouth_talk_01");

    public void SetToTalk02() => _characterMouth?.SetSprite("Mouth_talk_02");

    // --- Gestion de la parole ---

    public void StartTalking()
    {
        if (_talkRoutine != null) return;
        _talkRoutine = StartCoroutine(TalkRoutine());
    }

    public void StopTalking()
    {
        if (_talkRoutine != null)
        {
            StopCoroutine(_talkRoutine);
            _talkRoutine = null;
        }
        SetToNormal();
    }

    private IEnumerator TalkRoutine()
    {
        // Alterne entre les deux phases de parole pour un lip-sync dynamique
        while (true)
        {
            SetToTalk01();
            yield return new WaitForSeconds(_talkingSpeed);

            SetToTalk02();
            yield return new WaitForSeconds(_talkingSpeed);

            // Un petit passage par Normal ou Talk01 pour briser la répétition si tu veux
            if (Random.value > 0.7f)
            {
                SetToNormal();
                yield return new WaitForSeconds(_talkingSpeed / 2);
            }

            SetToTalk01();
            yield return new WaitForSeconds(_talkingSpeed);

            SetToNormal();
            yield return new WaitForSeconds(_talkingSpeed);
        }
    }
}