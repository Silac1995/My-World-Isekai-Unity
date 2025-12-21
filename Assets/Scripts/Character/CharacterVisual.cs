using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class CharacterVisual : MonoBehaviour
{
    private Character character;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private CharacterBodyPartsController bodyPartsController;
    [SerializeField] private BaseSpritesLibrarySO spritesLibrary;

    public BaseSpritesLibrarySO SpritesLibrary
    {
        get => spritesLibrary;
        set => spritesLibrary = value;
    }

    public CharacterBodyPartsController BodyPartsController => bodyPartsController;
    public CharacterBio CharacterBio => character.CharacterBio;
    public Character Character => character;

    
    private bool isFacingRight;
    public bool IsFacingRight => isFacingRight;

    // --- Constantes pour les limites de taille ---
    private const float MIN_SIZE = 95f;
    private const float MAX_SIZE = 105f;
    private const float NORMAL_SIZE = 100f;

    // --- Enum pour toutes les parties colorables ---
    public enum VisualPart
    {
        Skin,
        Hair,
        RightEye,
        LeftEye,
        RightSclera,
        LeftSclera
    }
    // --- Enum pour les SpriteResolvers filtrés ---
    public enum ResolverPart
    {
        Breasts,
        Eyes,
        Hair
    }

    // Associe chaque partie à une liste de SpriteRenderer
    private Dictionary<VisualPart, List<SpriteRenderer>> partRenderers;
    private Dictionary<ResolverPart, List<SpriteResolver>> spriteResolvers;



    // --- Attributs privés pour les couleurs ---
    private Color skinColor;
    private Color hairColor;
    private Color rightEyeColor;
    private Color leftEyeColor;
    private Color rightScleraColor;
    private Color leftScleraColor;

    // --- Propriétés publiques ---
    public Color SkinColor
    {
        get => skinColor;
        set
        {
            skinColor = value;
            ApplyColor(VisualPart.Skin, skinColor);
        }
    }

    public Color HairColor
    {
        get => hairColor;
        set
        {
            hairColor = value;
            ApplyColor(VisualPart.Hair, hairColor);
        }
    }

    public Color RightEyeColor
    {
        get => rightEyeColor;
        set
        {
            rightEyeColor = value;
            ApplyColor(VisualPart.RightEye, rightEyeColor);
        }
    }

    public Color LeftEyeColor
    {
        get => leftEyeColor;
        set
        {
            leftEyeColor = value;
            ApplyColor(VisualPart.LeftEye, leftEyeColor);
        }
    }

    public Color RightScleraColor
    {
        get => rightScleraColor;
        set
        {
            rightScleraColor = value;
            ApplyColor(VisualPart.RightSclera, rightScleraColor);
        }
    }

    public Color LeftScleraColor
    {
        get => leftScleraColor;
        set
        {
            leftScleraColor = value;
            ApplyColor(VisualPart.LeftSclera, leftScleraColor);
        }
    }

    private void Awake()
    {
        character = GetComponentInParent<Character>();
        if (character == null)
        {
            Debug.LogError("[CharacterVisual] Aucun Character trouvé dans les parents !");
        }

        InitializeSpriteRenderers();
        isFacingRight = true;
    }

    private void LateUpdate()
    {
        FaceCamera();
    }

    // --- Initialise les SpriteResolvers filtrés ---
    private void InitializeSpriteResolvers()
    {
        spriteResolvers = new Dictionary<ResolverPart, List<SpriteResolver>>();

        // Récupère tous les SpriteResolvers enfants (y compris inactifs)
        SpriteResolver[] allResolvers = GetComponentsInChildren<SpriteResolver>(true);

        foreach (var resolver in allResolvers)
        {
            if (resolver == null) continue;

            string lowerName = resolver.gameObject.name.ToLower();

            // Filtrage par nom pour assigner la partie correspondante
            if (lowerName.Contains("breast"))
            {
                AddResolver(ResolverPart.Breasts, resolver);
                Debug.Log($"[CharacterVisual] Ajout SpriteResolver Breasts: {resolver.gameObject.name}");
            }
            else if (lowerName.Contains("eye"))
            {
                AddResolver(ResolverPart.Eyes, resolver);
                Debug.Log($"[CharacterVisual] Ajout SpriteResolver Eyes: {resolver.gameObject.name}");
            }
            else if (lowerName.Contains("hair"))
            {
                AddResolver(ResolverPart.Hair, resolver);
                Debug.Log($"[CharacterVisual] Ajout SpriteResolver Hair: {resolver.gameObject.name}");
            }
            else
            {
                Debug.Log($"[CharacterVisual] SpriteResolver non assigné: {resolver.gameObject.name}");
            }
        }

        // Vérification pour chaque partie
        foreach (ResolverPart part in System.Enum.GetValues(typeof(ResolverPart)))
        {
            if (!spriteResolvers.ContainsKey(part) || spriteResolvers[part].Count == 0)
                Debug.LogWarning($"[CharacterVisual] Aucun SpriteResolver trouvé pour {part}");
        }
    }

    // --- Méthode utilitaire pour ajouter un SpriteResolver dans le dictionnaire ---
    private void AddResolver(ResolverPart part, SpriteResolver resolver)
    {
        if (!spriteResolvers.ContainsKey(part))
            spriteResolvers[part] = new List<SpriteResolver>();

        spriteResolvers[part].Add(resolver);
    }

    // --- Méthode publique pour récupérer une partie spécifique ---
    public List<SpriteResolver> GetResolvers(ResolverPart part)
    {
        if (spriteResolvers != null && spriteResolvers.ContainsKey(part))
            return spriteResolvers[part];

        return new List<SpriteResolver>();
    }

    private void FaceCamera()
    {
        if (Camera.main == null) return;

        Vector3 camForward = Camera.main.transform.forward;
        camForward.y = 0f;
        transform.forward = camForward;
    }

    public void UpdateFlip(Vector3 moveDir)
    {
        // On ne flip qu’en cas de déplacement horizontal
        if (moveDir.x > 0.01f)
            isFacingRight = true;
        else if (moveDir.x < -0.01f)
            isFacingRight = false;

        // On applique sans toucher si vertical uniquement
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1 : -1);
        transform.localScale = scale;
    }

    // --- Initialise le dictionnaire de SpriteRenderers ---
    public void InitializeSpriteRenderers()
    {
        partRenderers = new Dictionary<VisualPart, List<SpriteRenderer>>();

        foreach (VisualPart part in System.Enum.GetValues(typeof(VisualPart)))
            partRenderers[part] = new List<SpriteRenderer>();

        // Récupère tous les SpriteRenderers enfants
        SpriteRenderer[] allRenderers = GetComponentsInChildren<SpriteRenderer>();
        Debug.Log($"[CharacterVisual] Nombre total de SpriteRenderers trouvés: {allRenderers.Length}");

        foreach (var sr in allRenderers)
        {
            string name = sr.name.ToLower();
            string parentName = sr.transform.parent != null ? sr.transform.parent.name.ToLower() : "";

            // Debug pour TOUS les SpriteRenderers
            Debug.Log($"[CharacterVisual] SpriteRenderer: '{sr.name}' (parent: '{sr.transform.parent?.name}')");

            if (name.Contains("skin"))
            {
                Debug.Log($"[CharacterVisual] Ajout Skin: {sr.name}");
                partRenderers[VisualPart.Skin].Add(sr);
            }
            else if (name.Contains("hair"))
            {
                Debug.Log($"[CharacterVisual] Ajout Hair: {sr.name}");
                partRenderers[VisualPart.Hair].Add(sr);
            }
            else if (name == "eyebase" && parentName.Contains("eye_r"))
            {
                Debug.Log($"[CharacterVisual] Ajout RightEye: {sr.name}");
                partRenderers[VisualPart.RightEye].Add(sr);
            }
            else if (name == "eyebase" && parentName.Contains("eye_l"))
            {
                Debug.Log($"[CharacterVisual] Ajout LeftEye: {sr.name}");
                partRenderers[VisualPart.LeftEye].Add(sr);
            }
            else if (name == "eyesclera" && parentName.Contains("eye_r"))
            {
                Debug.Log($"[CharacterVisual] Ajout RightSclera: {sr.name}");
                partRenderers[VisualPart.RightSclera].Add(sr);
            }
            else if (name == "eyesclera" && parentName.Contains("eye_l"))
            {
                Debug.Log($"[CharacterVisual] Ajout LeftSclera: {sr.name}");
                partRenderers[VisualPart.LeftSclera].Add(sr);
            }
            else
            {
                Debug.Log($"[CharacterVisual] Non assigné: '{name}' (parent: '{parentName}')");
            }
        }

        // Debug si une partie n'a aucun sprite associé
        foreach (var kvp in partRenderers)
        {
            Debug.Log($"[CharacterVisual] {kvp.Key}: {kvp.Value.Count} SpriteRenderer(s)");
            if (kvp.Value.Count == 0)
                Debug.LogWarning($"[CharacterVisual] Aucun SpriteRenderer trouvé pour {kvp.Key}");
        }

        InitializeSpriteResolvers();
    }

    private void ApplyColor(VisualPart part, Color color)
    {
        Debug.Log($"[CharacterVisual] ApplyColor appelé pour {part} avec couleur {color}");

        if (!partRenderers.ContainsKey(part))
        {
            Debug.LogWarning($"[CharacterVisual] partRenderers ne contient pas la clé {part}");
            return;
        }

        Debug.Log($"[CharacterVisual] Nombre de SpriteRenderers pour {part}: {partRenderers[part].Count}");

        foreach (var spriteRenderer in partRenderers[part])
        {
            if (spriteRenderer != null)
            {
                Debug.Log($"[CharacterVisual] Application de {color} sur {spriteRenderer.name}. Ancienne couleur: {spriteRenderer.color}");
                spriteRenderer.color = color;
                Debug.Log($"[CharacterVisual] Nouvelle couleur appliquée: {spriteRenderer.color}");
            }
            else
            {
                Debug.LogWarning($"[CharacterVisual] SpriteRenderer null trouvé pour {part}");
            }
        }
    }

    [ContextMenu("Resize Collider")]
    public void ResizeColliderToSprite()
    {
        CapsuleCollider col = GetComponentInParent<CapsuleCollider>();
        if (col == null)
        {
            Debug.LogWarning("[CharacterVisual] Aucun CapsuleCollider trouvé. Ajout d'un nouveau.");
        }

        // Combine tous les bounds
        SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        if (spriteRenderers.Length == 0)
        {
            Debug.LogWarning("[CharacterVisual] Aucun SpriteRenderer trouvé pour redimensionner le collider !");
            return;
        }

        Bounds combinedBounds = spriteRenderers[0].bounds;
        for (int i = 1; i < spriteRenderers.Length; i++)
            combinedBounds.Encapsulate(spriteRenderers[i].bounds);

        col.height = combinedBounds.size.y;
        col.radius = Mathf.Max(combinedBounds.size.x, combinedBounds.size.z) / 2f;
        col.center = transform.InverseTransformPoint(combinedBounds.center);
        col.direction = 1; // Y axis
    }

    public void RandomizeBreastSprites(int totalLabels)
    {
        if (totalLabels < 1)
        {
            Debug.LogError("[CharacterVisual] totalLabels doit être >= 1");
            return;
        }

        var breastResolvers = GetResolvers(ResolverPart.Breasts);

        if (breastResolvers.Count == 0)
        {
            Debug.LogWarning("[CharacterVisual] Aucun SpriteResolver pour Breasts trouvé !");
            return;
        }

        // Choix unique d'un label pour tous les SpriteResolvers
        int randomIndex = Random.Range(1, totalLabels + 1);
        string randomLabel = randomIndex.ToString("D2"); // "01", "02", etc.

        foreach (var resolver in breastResolvers)
        {
            if (resolver == null) continue;

            string category = resolver.GetCategory();

            if (string.IsNullOrEmpty(category))
            {
                Debug.LogWarning($"[CharacterVisual] SpriteResolver {resolver.gameObject.name} n'a pas de catégorie !");
                continue;
            }

            resolver.SetCategoryAndLabel(category, randomLabel);
            Debug.Log($"[CharacterVisual] {resolver.gameObject.name} -> Category: {category}, Label: {randomLabel}");
        }
    }
    // --- Change aléatoirement les sprites des cheveux (tous au même label) ---
    public void RandomizeHairSprites(int totalLabels)
    {
        if (totalLabels < 1)
        {
            Debug.LogError("[CharacterVisual] totalLabels doit être >= 1");
            return;
        }

        var hairResolvers = GetResolvers(ResolverPart.Hair);

        if (hairResolvers.Count == 0)
        {
            Debug.LogWarning("[CharacterVisual] Aucun SpriteResolver pour Hair trouvé !");
            return;
        }

        // Choix unique d'un label pour tous les SpriteResolvers Hair
        int randomIndex = Random.Range(1, totalLabels + 1);
        string randomLabel = randomIndex.ToString("D2"); // "01", "02", etc.

        foreach (var resolver in hairResolvers)
        {
            if (resolver == null) continue;

            string category = resolver.GetCategory();

            if (string.IsNullOrEmpty(category))
            {
                Debug.LogWarning($"[CharacterVisual] SpriteResolver {resolver.gameObject.name} n'a pas de catégorie !");
                continue;
            }

            resolver.SetCategoryAndLabel(category, randomLabel);
            Debug.Log($"[CharacterVisual] {resolver.gameObject.name} -> Category: {category}, Label: {randomLabel}");
        }
    }

    /// <summary>
    /// Redimensionne une partie spécifique du personnage
    /// </summary>
    public void ResizeVisualPart(VisualPart part, float sizePercentage)
    {
        sizePercentage = Mathf.Clamp(sizePercentage, MIN_SIZE, MAX_SIZE);
        float scale = sizePercentage / 100f;

        var parentsToScale = GetParentsToScale(part);
        foreach (var parent in parentsToScale)
        {
            parent.localScale = Vector3.one * scale;
        }
    }

    /// <summary>
    /// Redimensionne tout le personnage
    /// </summary>
    public void ResizeCharacter(float sizePercentage)
    {
        sizePercentage = Mathf.Clamp(sizePercentage, MIN_SIZE, MAX_SIZE);
        float scale = sizePercentage / 100f;

        Transform target = visualRoot != null ? visualRoot : transform;
        target.localScale = Vector3.one * scale;
    }

    /// <summary>
    /// Obtient les parents à redimensionner pour une partie donnée
    /// </summary>
    private HashSet<Transform> GetParentsToScale(VisualPart part)
    {
        var parents = new HashSet<Transform>();

        if (!partRenderers.ContainsKey(part)) return parents;

        foreach (var sr in partRenderers[part])
        {
            if (sr == null) continue;

            Transform parent = GetScalableParent(sr.transform, part);
            if (parent != null) parents.Add(parent);
        }

        return parents;
    }

    /// <summary>
    /// Détermine le bon parent à redimensionner selon la partie
    /// </summary>
    private Transform GetScalableParent(Transform spriteTransform, VisualPart part)
    {
        return part switch
        {
            VisualPart.Hair => FindParentWithName(spriteTransform, "hair"),
            VisualPart.RightEye or VisualPart.RightSclera => FindParentWithName(spriteTransform, "eye_r"),
            VisualPart.LeftEye or VisualPart.LeftSclera => FindParentWithName(spriteTransform, "eye_l"),
            _ => spriteTransform
        };
    }

    /// <summary>
    /// Trouve un parent contenant un nom spécifique
    /// </summary>
    private Transform FindParentWithName(Transform current, string nameToFind)
    {
        while (current != null && current != transform)
        {
            if (current.name.ToLower().Contains(nameToFind.ToLower()))
                return current;
            current = current.parent;
        }
        return current?.parent ?? current;
    }

    // --- Méthodes de commodité ---
    public void ResizeHair(float size) => ResizeVisualPart(VisualPart.Hair, size);
    public void ResizeCharacterRelative(float variation) => ResizeCharacter(NORMAL_SIZE + Mathf.Clamp(variation, -15f, 30f));

    public void RandomizeAllPartSizes()
    {
        foreach (VisualPart part in System.Enum.GetValues(typeof(VisualPart)))
            ResizeVisualPart(part, Random.Range(MIN_SIZE, MAX_SIZE));
    }

    public void RandomizeCharacterSize() => ResizeCharacter(Random.Range(MIN_SIZE, MAX_SIZE));

    public void ResetAllPartSizes()
    {
        foreach (VisualPart part in System.Enum.GetValues(typeof(VisualPart)))
            ResizeVisualPart(part, NORMAL_SIZE);
    }

    public void ResetCharacterSize() => ResizeCharacter(NORMAL_SIZE);

    public void ResizeAllVisualParts(Dictionary<VisualPart, float> partSizes)
    {
        foreach (var kvp in partSizes)
            ResizeVisualPart(kvp.Key, kvp.Value);
    }

    public void InitializeSpriteLibraryAsset(BaseSpritesLibrarySO baseLibraryAsset)
    {
        SpritesLibrary = baseLibraryAsset;
        bodyPartsController.InitializeSpriteLibrariesToEveryBodyController();
    }
}