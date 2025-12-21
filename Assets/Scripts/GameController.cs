using UnityEngine;

public class GameController : MonoBehaviour
{
    [SerializeField] private SpawnManager spawnManager;
    [SerializeField] private GameObject playerCharacterGameObject;

    private void Update()
    {
        try
        {
            if (playerCharacterGameObject == null)
            {
                // Nouvelle méthode recommandée à partir d’Unity 2023+
                Character[] allCharacters = Object.FindObjectsByType<Character>(FindObjectsSortMode.None);

                if (allCharacters == null || allCharacters.Length == 0)
                {
                    //Debug.LogWarning("Aucun objet avec le script Character trouvé dans la scène.");
                    return;
                }

                foreach (Character character in allCharacters)
                {
                    if (character == null)
                        continue;

                    if (character.IsPlayer())
                    {
                        playerCharacterGameObject = character.gameObject;
                        Debug.Log("Personnage joueur détecté et assigné.");
                        break;
                    }
                }

                if (playerCharacterGameObject == null)
                {
                    //Debug.LogWarning("Aucun personnage joueur trouvé parmi les objets Character.");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Erreur lors de la détection du personnage joueur : {ex.Message}\n{ex.StackTrace}");
        }
    }
}
