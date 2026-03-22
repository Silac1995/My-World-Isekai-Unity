using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.U2D.Animation;



public class CharacterVisual : CharacterSystem
{
    [Header("Components")]

    [SerializeField] private Transform visualRoot;

    [SerializeField] private CharacterBodyPartsController bodyPartsController;

    [SerializeField] private BaseSpritesLibrarySO spritesLibrary;

    [SerializeField] private CharacterAnimator _characterAnimator;

    [SerializeField] private CharacterBlink characterBlink;



    private Coroutine _resizeCoroutine;
    private SpriteRenderer[] allRenderers;

    private NetworkVariable<bool> _netIsFacingRight = new NetworkVariable<bool>(
        true, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner);

    // --- Look Target : cible persistante pour orienter le regard ---
    private Transform _lookTarget;
    public Transform LookTarget => _lookTarget;
    public bool HasLookTarget => _lookTarget != null;

    // --- Anti-flicker : cooldown entre les flips ---
    private float _lastFlipTime = -999f;
    private const float FLIP_COOLDOWN = 0.15f;


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

    // Base class already provides: public Character Character => _character;

    public CharacterAnimator CharacterAnimator => _characterAnimator;

    public CharacterBlink CharacterBlink => characterBlink;

    public SpriteRenderer[] AllRenderers => allRenderers;

    // Dans CharacterVisual.cs

    public void ApplyPresetFromRace(RaceSO race)
    {
        Debug.Log($"<color=white>---> [CharacterVisual]</color> Entrée dans ApplyPresetFromRace pour {race?.raceName ?? "NULL"}");

        if (race == null)
        {
            Debug.LogError("[CharacterVisual] RaceSO passé est null !");
            return;
        }

        // VERIFICATION CRITIQUE : Vérifie le nom exact dans ton script RaceSO
        if (race.characterVisualPreset == null)
        {
            Debug.LogWarning($"[CharacterVisual] Aucun preset trouvé dans le SO de la race {race.raceName}. Vérifiez l'inspecteur.");
            return;
        }

        ApplyVisualPreset(race.characterVisualPreset);
    }

    public void ApplyVisualPreset(CharacterVisualPresetSO preset)
    {
        Debug.Log($"<color=cyan>[CharacterVisual]</color> Tentative d'application du preset: {preset.name}");

        if (preset is HumanoidVisualPresetSO humanoid)
        {
            ApplyHumanoidSettings(humanoid);
        }
        else
        {
            Debug.LogWarning($"[CharacterVisual] Le preset {preset.name} n'est pas de type Humanoid.");
        }
    }

    private void ApplyHumanoidSettings(HumanoidVisualPresetSO preset)
    {
        if (bodyPartsController == null) return;

        // 1. SÉCURITÉ : On s'assure que les dictionnaires de renderers sont prêts
        if (partRenderers == null || partRenderers[VisualPart.Skin].Count == 0)
        {
            InitializeSpriteRenderers();
        }

        // 2. Initialisation des membres (Ears, etc.)
        bodyPartsController.InitializeAllBodyParts();

        // 3. Application de la couleur de peau
        Debug.Log($"<color=orange>[Visual]</color> Application SkinColor: {preset.DefaultSkinColor}");
        this.SkinColor = preset.DefaultSkinColor;

        // 4. Application des oreilles (qui marchent déjà)
        if (bodyPartsController.EarsController != null)
        {
            bodyPartsController.EarsController.SetEarsCategory(preset.EarCategory);
            // On force aussi la couleur sur les oreilles spécifiquement
            bodyPartsController.EarsController.SetEarsColor(preset.DefaultSkinColor);
        }

        // 5. Application des mains
        if (bodyPartsController.HandsController != null)
        {
            bodyPartsController.HandsController.SetHandsCategory(preset.HandCategory);
            bodyPartsController.HandsController.SetHandsColor(preset.DefaultSkinColor);
        }
    }


    public bool IsFacingRight
    {
        get => _netIsFacingRight.Value;
        set
        {
            if (_netIsFacingRight.Value == value) return;

            // Bloquer le flip pendant un knockback
            if (_character != null && _character.CharacterMovement != null && _character.CharacterMovement.IsKnockedBack)
                return;

            // Bloquer le flip si le personnage est incapacité
            if (_character != null && _character.IsIncapacitated)
                return;

            // Anti-flicker : cooldown entre les flips
            if (Time.time - _lastFlipTime < FLIP_COOLDOWN) return;

            // Only the owner can change the NetworkVariable
            if (IsOwner)
            {
                _netIsFacingRight.Value = value;
                _lastFlipTime = Time.time;
                ApplyFlip();
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _netIsFacingRight.OnValueChanged += OnFacingDirectionChanged;
        ApplyFlip(); 
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _netIsFacingRight.OnValueChanged -= OnFacingDirectionChanged;
    }

    private void OnFacingDirectionChanged(bool previousValue, bool newValue)
    {
        if (!IsOwner)
        {
            _lastFlipTime = Time.time;
            ApplyFlip();
        }
    }

    #endregion



    protected override void Awake()

    {

        base.Awake();

        if (_character == null) Debug.LogError("[CharacterVisual] Aucun Character trouvé !");



        InitializeSpriteRenderers();
        if (characterBlink == null) characterBlink = GetComponent<CharacterBlink>();
    }

    private void LateUpdate()
    {
        // Si un look target est défini, on oriente le sprite vers lui chaque frame
        if (_lookTarget != null)
        {
            FaceTarget(_lookTarget.position);
        }
    }



    #region Flip & Orientation Logic



    /// <summary>

    /// Oriente le personnage vers une position cible (utile pour les interactions)

    /// </summary>

    public void FaceTarget(Vector3 targetPosition)
    {
        // Zone morte plus large pour éviter le flip-flop quand la cible est quasi alignée en X
        float direction = targetPosition.x - transform.position.x;

        if (Mathf.Abs(direction) > 0.3f)
        {
            IsFacingRight = (direction > 0);
        }
    }

    /// <summary>
    /// Oriente le personnage vers un autre personnage.
    /// </summary>
    public void FaceCharacter(Character target)
    {
        if (target == null) return;
        FaceTarget(target.transform.position);
    }

    /// <summary>
    /// Définit une cible persistante pour orienter le regard.
    /// Le sprite s'orientera automatiquement vers cette cible chaque frame.
    /// </summary>
    public void SetLookTarget(Transform target)
    {
        _lookTarget = target;
    }

    /// <summary>
    /// Définit un Character comme cible de regard persistante.
    /// </summary>
    public void SetLookTarget(Character target)
    {
        _lookTarget = target != null ? target.transform : null;
    }

    /// <summary>
    /// Retire la cible de regard. Le flip redevient contrôlé par le mouvement.
    /// </summary>
    public void ClearLookTarget()
    {
        _lookTarget = null;
    }

    protected override void HandleIncapacitated(Character c)
    {
        ClearLookTarget();
    }

    public void UpdateFlip(Vector3 moveDir)
    {
        // Si un look target est actif, le mouvement ne contrôle pas l'orientation
        if (_lookTarget != null) return;

        if (Mathf.Abs(moveDir.x) > 0.01f)
        {
            IsFacingRight = (moveDir.x > 0);
        }
    }

    /// <summary>
    /// Détermine si le personnage marche vers l'avant ou l'arrière par rapport à son orientation.
    /// Utilisé principalement quand un LookTarget est actif.
    /// </summary>
    public void UpdateWalkingParameters(Vector3 velocity)
    {
        if (_characterAnimator == null) return;

        float speed = new Vector3(velocity.x, 0, velocity.z).magnitude;
        bool isWalkingForward = false;
        bool isWalkingBackward = false;

        if (speed > 0.1f)
        {
            if (_lookTarget != null)
            {
                float moveX = velocity.x;
                float facingDir = IsFacingRight ? 1f : -1f;

                if (Mathf.Abs(moveX) > 0.1f)
                {
                    // Si le mouvement X est dans le même sens que l'orientation -> Forward
                    // Sinon -> Backward
                    if (Mathf.Sign(moveX) == Mathf.Sign(facingDir))
                        isWalkingForward = true;
                    else
                        isWalkingBackward = true;
                }
                else
                {
                    // Mouvement purement vertical (Z) alors qu'on regarde une cible sur le côté
                    // On considère ça comme une marche avant par défaut
                    isWalkingForward = true;
                }
            }
            else
            {
                // Pas de cible : marche normale vers l'avant
                isWalkingForward = true;
            }
        }

        _characterAnimator.SetWalkingForward(isWalkingForward);
        _characterAnimator.SetWalkingBackward(isWalkingBackward);
    }

    public bool HasALookTarget() => _lookTarget != null;

    private void ApplyFlip()

    {

        // On applique le scale sur le transform local (ou le visualRoot si tu préfères)

        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (IsFacingRight ? 1 : -1);

        transform.localScale = scale;

    }



    #endregion



    #region Initialization



    public void InitializeSpriteRenderers()

    {

        partRenderers = new Dictionary<VisualPart, List<SpriteRenderer>>();

        foreach (VisualPart part in System.Enum.GetValues(typeof(VisualPart)))

            partRenderers[part] = new List<SpriteRenderer>();



        allRenderers = GetComponentsInChildren<SpriteRenderer>(true);



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



    // Appelle ça à la fin de ton SpawnCharacter
    public void RequestAutoResize()
    {
        if (_resizeCoroutine != null)
            StopCoroutine(_resizeCoroutine);

        _resizeCoroutine = StartCoroutine(ResizeRoutine());
    }

    private IEnumerator ResizeRoutine()
    {
        // On attend que Unity ait fini de calculer les sprites et le rendu
        yield return new WaitForEndOfFrame();

        ResizeColliderToSprite();

        _resizeCoroutine = null;
    }

    [ContextMenu("Resize Collider")]
    public void ResizeColliderToSprite()
    {
        CapsuleCollider col = GetComponentInParent<CapsuleCollider>();
        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>(false);
        if (srs.Length == 0 || col == null) return;

        // 1. Calcul des limites visuelles (Bounds)
        Bounds b = srs[0].bounds;
        foreach (var sr in srs) b.Encapsulate(sr.bounds);

        // 2. Réinitialisation de la position locale
        // On force le visuel à être pile sur le pivot du parent (0,0,0)
        transform.localPosition = Vector3.zero;

        // 3. Calcul de la hauteur
        // On mesure la distance entre le bas réel du sprite et le haut réel du sprite
        float height = b.size.y;
        col.height = height;
        col.radius = 0.75f;

        // 4. Positionnement du Collider
        // On le place pour qu'il commence à 0 et monte vers le haut
        // On ne se base plus sur les bounds pour le centre, mais sur la hauteur pure
        col.center = new Vector3(0, height / 2f, 0);

        Debug.Log("<color=white>[Visual]</color> Offset supprimé. Pivot forcé à (0,0,0).");
    }

    public Vector3 GetVisualExtremity(Vector3 direction)
    {
        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>(false);
        // On filtre pour exclure les ombres ou autres éléments utilitaires qui fausseraient les bounds réels du corps
        var filtered = srs.Where(s => !s.name.ToLower().Contains("shadow")).ToList();
        
        if (filtered.Count == 0) return transform.position;

        Bounds b = filtered[0].bounds;
        foreach (var sr in filtered) b.Encapsulate(sr.bounds);

        Vector3 center = b.center;
        Vector3 extents = b.extents;

        return new Vector3(
            center.x + (direction.x > 0 ? extents.x : (direction.x < 0 ? -extents.x : 0)),
            center.y + (direction.y > 0 ? extents.y : (direction.y < 0 ? -extents.y : 0)),
            center.z + (direction.z > 0 ? extents.z : (direction.z < 0 ? -extents.z : 0))
        );
    }

    public int GetMaxSortingOrder()
    {
        if (allRenderers == null || allRenderers.Length == 0) return 0;
        return allRenderers.Max(sr => sr.sortingOrder);
    }

    public Bounds GetVisualBounds()
    {
        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>(false);
        var filtered = srs.Where(s => !s.name.ToLower().Contains("shadow") && !s.name.ToLower().Contains("wound")).ToList();
        
        if (filtered.Count == 0) return new Bounds(transform.position, Vector3.zero);

        Bounds b = filtered[0].bounds;
        foreach (var sr in filtered) b.Encapsulate(sr.bounds);
        return b;
    }

    public Vector3 GetVisualCenter()
    {
        return GetVisualBounds().center;
    }

    #endregion

}