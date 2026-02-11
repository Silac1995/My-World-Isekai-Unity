using UnityEngine;

public class CharacterSpeech : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private GameObject _speechBubblePrefab; // Ta bulle de texte
    //ajouter character mouth body part

    // Tu pourras l'appeler depuis tes Behaviours ou tes Interactions
    public void Say(string message, float duration = 3f)
    {
        // 1. Afficher le texte
        // 2. Lancer un timer pour le cacher
        Debug.Log($"{_character.CharacterName} dit : {message}");
    }
}