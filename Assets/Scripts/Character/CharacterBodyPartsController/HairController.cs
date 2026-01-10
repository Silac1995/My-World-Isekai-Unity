using UnityEngine;

public class HairController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterBodyPartsController bodyPartsController;

    [Header("Hair Data")]
    [SerializeField] private CharacterHair characterHair;
    [SerializeField] private CharacterPubicHair characterPubicHair;

    [Header("Settings")]
    [SerializeField] private string spriteLibraryHairCategory = "01";
    [SerializeField] private string spriteLibraryPubicHairCategory = "01";

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;

    public void Initialize()
    {
        RetrieveHairObjects();
    }

    private void RetrieveHairObjects()
    {
        // Nettoyage des références au cas où
        characterHair = null;
        characterPubicHair = null;

        // Cible le conteneur Humanoid_Base
        Transform spritesContainer = (transform.childCount > 0) ? transform.GetChild(0) : transform;
        SpriteRenderer[] allRenderers = spritesContainer.GetComponentsInChildren<SpriteRenderer>(true);

        if (debugMode) Debug.Log($"<color=orange>[HairController]</color> Filtering hair parts in {spritesContainer.name}...");

        foreach (var renderer in allRenderers)
        {
            GameObject part = renderer.gameObject;
            string name = part.name; // On garde le nom original pour voir les majuscules si besoin
            string lowerName = name.ToLower();

            // On ne traite QUE les objets commençant par "hair_"
            if (!lowerName.StartsWith("hair_")) continue;

            // 1. Cas spécifique : Poils pubiens
            if (lowerName == "hair_pubic")
            {
                characterPubicHair = new CharacterPubicHair(
                    bodyPartsController,
                    part,
                    spriteLibraryPubicHairCategory,
                    "Hair"
                );
                if (debugMode) Debug.Log("<color=orange>[HairController]</color> Assigned: <b>Hair_Pubic</b>");
            }
            // 2. Cas spécifique : Sourcils (On les ignore car EyesController s'en occupe)
            else if (lowerName.Contains("eyebrow"))
            {
                continue;
            }
            // 3. Cas général : Les cheveux (tout ce qui est "Hair_..." mais pas pubic ni eyebrow)
            else
            {
                // Ici on peut imaginer "Hair_Front", "Hair_Back", etc.
                // Pour l'instant on initialise ton objet principal CharacterHair
                characterHair = new CharacterHair(
                    bodyPartsController,
                    part,
                    spriteLibraryHairCategory,
                    "01"
                );
                if (debugMode) Debug.Log($"<color=orange>[HairController]</color> Assigned: <b>{name}</b> as Main Hair");
            }
        }

        if (debugMode && characterHair == null)
            Debug.LogWarning("<color=red>[HairController]</color> No main hair found (starting with Hair_ and not being pubic/eyebrow)");
    }

    // --- Gestion des Couleurs ---

    /// <summary>
    /// Change la couleur de la chevelure principale.
    /// </summary>
    public void SetMainHairColor(Color color)
    {
        if (characterHair != null)
        {
            characterHair.SetColor(color);
        }
        else if (debugMode)
        {
            Debug.LogWarning("[HairController] Impossible de changer la couleur : characterHair est null.");
        }
    }

    /// <summary>
    /// Change la couleur de la pilosité pubienne.
    /// </summary>
    public void SetPubicHairColor(Color color)
    {
        if (characterPubicHair != null)
        {
            characterPubicHair.SetColor(color);
        }
        else if (debugMode)
        {
            Debug.LogWarning("[HairController] Impossible de changer la couleur : characterPubicHair est null.");
        }
    }

    /// <summary>
    /// Change la couleur de TOUS les poils et cheveux gérés par ce script.
    /// Utile pour l'initialisation d'un NPC ou le spawn.
    /// </summary>
    public void SetGlobalHairColor(Color color)
    {
        SetMainHairColor(color);
        SetPubicHairColor(color);

        if (debugMode) Debug.Log("<color=orange>[HairController]</color> Global hair color updated.");
    }
}