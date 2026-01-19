using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class EarsController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;
    [SerializeField] private List<CharacterEar> _ears = new List<CharacterEar>();
    [SerializeField] private SpriteLibraryAsset _spriteLibraryAssetsEars;

    [Header("Settings")]
    [SerializeField] private string _spriteLibraryCategory = "01";
    [SerializeField] private bool _debugMode = true;

    public void Initialize()
    {
        RetrieveEarObjects();
    }

    private void RetrieveEarObjects()
    {
        _ears.Clear();

        // On s'assure de bien viser le conteneur. 
        // Si tes sprites sont dans un enfant, transform.GetChild(0) est risqué si l'ordre change.
        Transform spritesContainer = (transform.childCount > 0) ? transform.GetChild(0) : transform;

        // Debug pour vérifier où on cherche
        if (_debugMode) Debug.Log($"<color=cyan>[EarsController]</color> Recherche dans : {spritesContainer.name}");

        SpriteRenderer[] allRenderers = spritesContainer.GetComponentsInChildren<SpriteRenderer>(true);

        if (allRenderers.Length == 0 && _debugMode)
            Debug.LogWarning($"<color=red>[EarsController]</color> Aucun SpriteRenderer trouvé dans {spritesContainer.name} !");

        foreach (var renderer in allRenderers)
        {
            GameObject part = renderer.gameObject;
            string lowerName = part.name.ToLower();

            // LOG DE TOUS LES NOMS TROUVÉS pour debug
            // if (_debugMode) Debug.Log($"Vérification de : {lowerName}");

            // Utilisation de .Contains() sur le nom exact que tu as donné
            if (lowerName.Contains("skin_ear") && !lowerName.Contains("ring"))
            {
                // Détection du côté
                string side = lowerName.Contains("_l") ? "L" : "R";
                string label = $"Ear_{side}";

                CharacterEar newEar = new CharacterEar(
                    _bodyPartsController,
                    part,
                    _spriteLibraryCategory,
                    label
                );

                _ears.Add(newEar);

                if (_debugMode) Debug.Log($"<color=green>[EarsController]</color> Succès ! Ajouté : <b>{part.name}</b> avec le label {label}");
            }
        }

        if (_ears.Count == 0 && _debugMode)
            Debug.LogError($"<color=red>[EarsController]</color> Scan terminé : 0 oreilles trouvées. Vérifie le nom des GameObjects.");
    }

    public void SetEarsColor(Color color)
    {
        foreach (var ear in _ears)
        {
            ear.SetColor(color);
        }
    }
    // Dans EarsController.cs

    public void SetEarsCategory(string categoryName)
    {
        _spriteLibraryCategory = categoryName;

        // SECURITÉ : Si la liste est vide au moment de l'appel, on tente un scan rapide
        if (_ears.Count == 0)
        {
            Debug.LogWarning("<color=orange>[EarsController]</color> Liste vide, tentative de scan forcé.");
            RetrieveEarObjects();
        }

        foreach (var ear in _ears)
        {
            ear.SetCategory(categoryName);
        }
    }

}