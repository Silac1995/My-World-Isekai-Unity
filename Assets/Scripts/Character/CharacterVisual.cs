using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class CharacterVisual : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private CharacterBodyPartsController bodyPartsController;
    [SerializeField] private BaseSpritesLibrarySO spritesLibrary;

    private Character character;
    private bool isFacingRight = true;

    // --- Dictionnaires ---
    private Dictionary<VisualPart, List<SpriteRenderer>> partRenderers;
    private Dictionary<ResolverPart, List<SpriteResolver>> spriteResolvers;

    // --- Enums & Constantes ---
    public enum VisualPart { Skin, Hair, RightEye, LeftEye, RightSclera, LeftSclera }
    public enum ResolverPart { Breasts, Eyes, Hair }

    private const float MIN_SIZE = 98f;
    private const float MAX_SIZE = 102f;
    private const float NORMAL_SIZE = 100f;

    #region Properties
    public BaseSpritesLibrarySO SpritesLibrary { get => spritesLibrary; set => spritesLibrary = value; }
    public CharacterBodyPartsController BodyPartsController => bodyPartsController;
    public Character Character => character;

    public bool IsFacingRight
    {
        get => isFacingRight;
        set
        {
            if (isFacingRight == value) return;
            isFacingRight = value;
            ApplyFlip();
        }
    }
    #endregion

    private void Awake()
    {
        character = GetComponentInParent<Character>();
        if (character == null) Debug.LogError("[CharacterVisual] Aucun Character trouvé !");

        InitializeSpriteRenderers();
    }

    #region Flip & Orientation Logic

    /// <summary>
    /// Oriente le personnage vers une position cible (utile pour les interactions)
    /// </summary>
    public void FaceTarget(Vector3 targetPosition)
    {
        // On compare les positions sur l'axe X
        float direction = targetPosition.x - transform.position.x;

        if (Mathf.Abs(direction) > 0.01f)
        {
            IsFacingRight = (direction > 0);
        }
    }

    public void UpdateFlip(Vector3 moveDir)
    {
        if (Mathf.Abs(moveDir.x) > 0.01f)
        {
            IsFacingRight = (moveDir.x > 0);
        }
    }

    private void ApplyFlip()
    {
        // On applique le scale sur le transform local (ou le visualRoot si tu préfères)
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1 : -1);
        transform.localScale = scale;
    }

    #endregion

    #region Initialization

    public void InitializeSpriteRenderers()
    {
        partRenderers = new Dictionary<VisualPart, List<SpriteRenderer>>();
        foreach (VisualPart part in System.Enum.GetValues(typeof(VisualPart)))
            partRenderers[part] = new List<SpriteRenderer>();

        SpriteRenderer[] allRenderers = GetComponentsInChildren<SpriteRenderer>(true);

        foreach (var sr in allRenderers)
        {
            string name = sr.name.ToLower();
            string parentName = sr.transform.parent != null ? sr.transform.parent.name.ToLower() : "";

            if (name.Contains("skin")) AssignPart(VisualPart.Skin, sr);
            else if (name.Contains("hair")) AssignPart(VisualPart.Hair, sr);
            else if (name == "eyebase")
            {
                if (parentName.Contains("eye_r")) AssignPart(VisualPart.RightEye, sr);
                else if (parentName.Contains("eye_l")) AssignPart(VisualPart.LeftEye, sr);
            }
            else if (name == "eyesclera")
            {
                if (parentName.Contains("eye_r")) AssignPart(VisualPart.RightSclera, sr);
                else if (parentName.Contains("eye_l")) AssignPart(VisualPart.LeftSclera, sr);
            }
        }

        InitializeSpriteResolvers();
    }

    private void AssignPart(VisualPart part, SpriteRenderer sr) => partRenderers[part].Add(sr);

    private void InitializeSpriteResolvers()
    {
        spriteResolvers = new Dictionary<ResolverPart, List<SpriteResolver>>();
        SpriteResolver[] allResolvers = GetComponentsInChildren<SpriteResolver>(true);

        foreach (var resolver in allResolvers)
        {
            string lowerName = resolver.gameObject.name.ToLower();
            if (lowerName.Contains("breast")) AddResolver(ResolverPart.Breasts, resolver);
            else if (lowerName.Contains("eye")) AddResolver(ResolverPart.Eyes, resolver);
            else if (lowerName.Contains("hair")) AddResolver(ResolverPart.Hair, resolver);
        }
    }

    private void AddResolver(ResolverPart part, SpriteResolver resolver)
    {
        if (!spriteResolvers.ContainsKey(part)) spriteResolvers[part] = new List<SpriteResolver>();
        spriteResolvers[part].Add(resolver);
    }
    #endregion

    #region Visual Customization (Colors & Sizes)

    public void ApplyColor(VisualPart part, Color color)
    {
        if (!partRenderers.TryGetValue(part, out var renderers)) return;
        foreach (var sr in renderers) if (sr != null) sr.color = color;
    }

    public void ResizeCharacter(float sizePercentage)
    {
        float scale = Mathf.Clamp(sizePercentage, MIN_SIZE, MAX_SIZE) / 100f;
        Transform target = visualRoot != null ? visualRoot : transform;

        // On préserve le signe du scale X pour ne pas casser le Flip en cours
        float currentFlip = Mathf.Sign(target.localScale.x);
        target.localScale = new Vector3(scale * currentFlip, scale, scale);
    }

    // Encapsulation des couleurs pour les propriétés
    public Color SkinColor { get => skinColor; set { skinColor = value; ApplyColor(VisualPart.Skin, value); } }
    private Color skinColor;
    // ... (Répéter le pattern pour les autres couleurs si nécessaire)

    #endregion

    #region Helpers & Tooling

    public List<SpriteResolver> GetResolvers(ResolverPart part)
        => spriteResolvers.TryGetValue(part, out var list) ? list : new List<SpriteResolver>();

    [ContextMenu("Resize Collider")]
    public void ResizeColliderToSprite()
    {
        CapsuleCollider col = GetComponentInParent<CapsuleCollider>();
        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>();
        if (srs.Length == 0 || col == null) return;

        Bounds b = srs[0].bounds;
        for (int i = 1; i < srs.Length; i++) b.Encapsulate(srs[i].bounds);

        col.height = b.size.y;
        col.radius = Mathf.Max(b.size.x, b.size.z) / 2f;
        col.center = transform.InverseTransformPoint(b.center);
    }
    #endregion
}