using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class EyesController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterBodyPartsController bodyPartsController;
    [SerializeField] private List<CharacterEye> eyes= new List<CharacterEye>();

    [SerializeField] private string spriteLibraryEyesCategory = "01";
    [SerializeField] private SpriteLibraryAsset spriteLibraryAssetsEyes;
    [SerializeField] private SpriteLibraryAsset spriteLibraryAssetsEyebrows;


    [Header("Blink Settings")]
    [SerializeField] private bool enableAutoBlink = true;
    [SerializeField] private float minBlinkInterval = 0.10f;
    [SerializeField] private float maxBlinkInterval = 5f;
    [SerializeField] private float blinkDuration = 0.15f;
    private Coroutine blinkRoutine;

    [Header("Debug")]
    [SerializeField] private bool debugMode = true;

    private void Awake()
    {
        ValidateReferences();
    }

    public void Initialize()
    {
        RetrieveEyeGameObjects();
    }

    public void InitializeSpriteLibraries()
    {
        // Get the CharacterVisual component attached to THIS GameObject
        CharacterVisual cv = GetComponent<CharacterVisual>();

        if (cv == null)
        {
            Debug.LogError("CharacterVisual component not found on this GameObject!");
            return;
        }

        // Access its sprite libraries
        spriteLibraryAssetsEyes = cv.SpritesLibrary.Body_EyesLibrary;
        spriteLibraryAssetsEyebrows = cv.SpritesLibrary.Body_EyebrowsLibrary;
    }

    private void RetrieveEyeGameObjects()
    {
        eyes.Clear();

        // On cible le conteneur des sprites (ex: Humanoid_Base)
        Transform spritesContainer = (transform.childCount > 0) ? transform.GetChild(0) : transform;

        // Dictionnaires pour coupler les pièces par côté
        var bases = new Dictionary<string, GameObject>();
        var pupils = new Dictionary<string, GameObject>();
        var scleras = new Dictionary<string, GameObject>();
        var eyebrows = new Dictionary<string, GameObject>();

        // On récupère uniquement les objets ayant un SpriteRenderer (on ignore les os/bones)
        SpriteRenderer[] allRenderers = spritesContainer.GetComponentsInChildren<SpriteRenderer>(true);

        if (debugMode) Debug.Log($"<color=cyan>[EyesController]</color> Scanning {allRenderers.Length} sprites in {spritesContainer.name}...");

        foreach (SpriteRenderer renderer in allRenderers)
        {
            GameObject part = renderer.gameObject;
            string lowerName = part.name.ToLower();
            string side = "";

            // Détection du côté (flexible : gère _L, _l, Eye_L, etc.)
            if (lowerName.Contains("_l")) side = "L";
            else if (lowerName.Contains("_r")) side = "R";

            if (string.IsNullOrEmpty(side)) continue;

            // Tri des pièces par mots-clés
            if (lowerName.Contains("eyebase") || lowerName.Contains("eye_base")) bases[side] = part;
            else if (lowerName.Contains("pupil")) pupils[side] = part;
            else if (lowerName.Contains("sclera")) scleras[side] = part;
            else if (lowerName.Contains("eyebrow")) eyebrows[side] = part;
        }

        // Assemblage final avec Debugs de vérification
        string[] sides = { "L", "R" };
        foreach (string s in sides)
        {
            if (bases.ContainsKey(s))
            {
                // Construction des labels pour la Sprite Library
                string eyeLabel = $"Eye_{s}";
                string eyebrowLabel = $"Eyebrow_{s}";

                // Dans ta méthode RetrieveEyeGameObjects, au moment du "new" :
                CharacterEye newEye = new CharacterEye(
                    bodyPartsController, // On passe la référence ici
                    eyebrows.ContainsKey(s) ? eyebrows[s] : null,
                    bases[s],
                    scleras.ContainsKey(s) ? scleras[s] : null,
                    pupils.ContainsKey(s) ? pupils[s] : null,
                    eyeLabel,
                    eyebrowLabel,
                    spriteLibraryEyesCategory,
                    "01"
                );

                eyes.Add(newEye);

                if (debugMode)
                    Debug.Log($"<color=green>[EyesController]</color> Eye <b>{s}</b> assembled successfully. " +
                              $"(Sclera: {scleras.ContainsKey(s)}, Pupil: {pupils.ContainsKey(s)})");
            }
            else if (debugMode)
            {
                Debug.LogWarning($"<color=yellow>[EyesController]</color> Side <b>{s}</b> was skipped (no EyeBase found).");
            }
        }

        if (debugMode) Debug.Log($"<color=cyan>[EyesController]</color> Initialization finished. Total eyes: {eyes.Count}");
    }


    private Transform FindChildIgnoreCase(Transform parent, string nameContains)
    {
        nameContains = nameContains.ToLower();
        foreach (Transform child in parent)
        {
            if (child.name.ToLower().Contains(nameContains))
                return child;
        }
        return null;
    }
    private void ValidateReferences()
    {
        if (bodyPartsController == null)
        {
            Debug.LogWarning($"EyesController on {gameObject.name}: CharacterBodyPartsController reference is missing! Please assign it in the Inspector.");
        }
        else if (debugMode)
        {
            Debug.Log($"EyesController on {gameObject.name}: Successfully linked to CharacterBodyPartsController on '{bodyPartsController.gameObject.name}'");
        }
    }

    // eyes blinking
    private void OnEnable()
    {
        if (enableAutoBlink && blinkRoutine == null)
        {
            blinkRoutine = StartCoroutine(BlinkRoutine());
        }
    }

    private void OnDisable()
    {
        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            blinkRoutine = null;
        }
    }

    private IEnumerator BlinkRoutine()
    {
        while (enableAutoBlink)
        {
            // Attente avant le prochain clignement
            float waitTime = Random.Range(minBlinkInterval, maxBlinkInterval);
            yield return new WaitForSeconds(waitTime);

            // Fermer les yeux
            SetEyesClosed(true);

            // Maintenir fermé pendant blinkDuration
            yield return new WaitForSeconds(blinkDuration);

            // Réouvrir
            SetEyesClosed(false);
        }
    }

    private void SetEyesClosed(bool closed)
    {
        foreach (var eye in eyes)
        {
            if (eye != null)
            {
                eye.SetClosed(closed);
            }
        }
    }

    /// <summary>
    /// Change la couleur d'un œil spécifique dans la liste des eyes.
    /// </summary>
    /// <param name="eyeIndex">Index de l'œil dans la liste (0-based).</param>
    /// <param name="color">Couleur à appliquer à la pupille de l'œil.</param>
    public void SetEyePupilColor(int eyeIndex, Color color)
    {
        if (eyeIndex < 0 || eyeIndex >= eyes.Count)
        {
            Debug.LogWarning($"EyesController: Invalid eyeIndex {eyeIndex}. Must be between 0 and {eyes.Count - 1}.");
            return;
        }

        CharacterEye targetEye = eyes[eyeIndex];
        if (targetEye != null)
        {
            targetEye.SetPupilColor(color);
        }
        else
        {
            Debug.LogWarning($"EyesController: CharacterEye at index {eyeIndex} is null.");
        }
    }

    /// <summary>
    /// Change la couleur de toutes les pupilles de tous les yeux.
    /// </summary>
    /// <param name="color">La couleur à appliquer aux pupilles.</param>
    public void SetAllPupilsColor(Color color)
    {
        for (int i = 0; i < eyes.Count; i++)
        {
            CharacterEye eye = eyes[i];
            if (eye != null)
            {
                eye.SetPupilColor(color);
            }
            else if (debugMode)
            {
                Debug.LogWarning($"EyesController: CharacterEye at index {i} is null.");
            }
        }
    }
    // Définit l'expression des sourcils pour tous les yeux
    public void SetEyebrowsExpression(string expressionSuffix)
    {
        foreach (var eye in eyes)
        {
            eye.SetEyebrowState(expressionSuffix);
        }
    }

    // Méthodes d'appel simplifiées
    [ContextMenu("Set Eyebrows to Normal")]
    public void SetEyebrowsToNormal() => SetEyebrowsExpression("_normal");

    [ContextMenu("Set Eyebrows to Frowned")]
    public void SetEyebrowsToFrowned() => SetEyebrowsExpression("_frowned");

    [ContextMenu("Set Eyebrows to Raised")]
    public void SetEyebrowsToRaised() => SetEyebrowsExpression("_raised");

    [ContextMenu("Set Eyebrows to Worried")]
    public void SetEyebrowsToWorried() => SetEyebrowsExpression("_worried");



}