using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.U2D.Animation;



public class CharacterVisual : CharacterSystem, ICharacterVisual, IAnimationLayering
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

    // --- Look Target: persistent target for orientation ---
    private Transform _lookTarget;
    public Transform LookTarget => _lookTarget;
    public bool HasLookTarget => _lookTarget != null;

    // --- Anti-flicker: cooldown between flips ---
    private float _lastFlipTime = -999f;
    private const float FLIP_COOLDOWN = 0.15f;


    // --- Dictionaries ---

    private Dictionary<VisualPart, List<SpriteRenderer>> partRenderers;

    private Dictionary<ResolverPart, List<SpriteResolver>> spriteResolvers;



    // --- Enums & Constants ---

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

    public void ApplyPresetFromRace(RaceSO race)
    {
        Debug.Log($"<color=white>---> [CharacterVisual]</color> Entering ApplyPresetFromRace for {race?.raceName ?? "NULL"}");

        if (race == null)
        {
            Debug.LogError("[CharacterVisual] RaceSO passed is null!");
            return;
        }

        // CRITICAL CHECK: Verify the exact name in the RaceSO script
        if (race.characterVisualPreset == null)
        {
            Debug.LogWarning($"[CharacterVisual] No preset found in the race SO for {race.raceName}. Check the inspector.");
            return;
        }

        ApplyVisualPreset(race.characterVisualPreset);
    }

    public void ApplyVisualPreset(CharacterVisualPresetSO preset)
    {
        Debug.Log($"<color=cyan>[CharacterVisual]</color> Attempting to apply preset: {preset.name}");

        if (preset is HumanoidVisualPresetSO humanoid)
        {
            ApplyHumanoidSettings(humanoid);
        }
        else
        {
            Debug.LogWarning($"[CharacterVisual] Preset {preset.name} is not of type Humanoid.");
        }
    }

    private void ApplyHumanoidSettings(HumanoidVisualPresetSO preset)
    {
        if (bodyPartsController == null) return;

        // 1. SAFETY: Ensure renderer dictionaries are ready
        if (partRenderers == null || partRenderers[VisualPart.Skin].Count == 0)
        {
            InitializeSpriteRenderers();
        }

        // 2. Initialize body parts (Ears, etc.)
        bodyPartsController.InitializeAllBodyParts();

        // 3. Apply skin color
        Debug.Log($"<color=orange>[Visual]</color> Application SkinColor: {preset.DefaultSkinColor}");
        this.SkinColor = preset.DefaultSkinColor;

        // 4. Apply ears (already working)
        if (bodyPartsController.EarsController != null)
        {
            bodyPartsController.EarsController.SetEarsCategory(preset.EarCategory);
            // Also force color on ears specifically
            bodyPartsController.EarsController.SetEarsColor(preset.DefaultSkinColor);
        }

        // 5. Apply hands
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

            // Block flip during knockback
            if (_character != null && _character.CharacterMovement != null && _character.CharacterMovement.IsKnockedBack)
                return;

            // Block flip if the character is incapacitated
            if (_character != null && _character.IsIncapacitated)
                return;

            // Anti-flicker: cooldown between flips
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

        if (_character == null) Debug.LogError("[CharacterVisual] No Character found!");



        InitializeSpriteRenderers();
        if (characterBlink == null) characterBlink = GetComponent<CharacterBlink>();
    }

    private void LateUpdate()
    {
        // If a look target is set, orient the sprite toward it each frame
        if (_lookTarget != null)
        {
            FaceTarget(_lookTarget.position);
        }
    }



    #region Flip & Orientation Logic



    /// <summary>

    /// Orients the character toward a target position (useful for interactions)

    /// </summary>

    public void FaceTarget(Vector3 targetPosition)
    {
        // Larger dead zone to avoid flip-flop when the target is nearly aligned on X
        float direction = targetPosition.x - transform.position.x;

        if (Mathf.Abs(direction) > 0.3f)
        {
            IsFacingRight = (direction > 0);
        }
    }

    /// <summary>
    /// Orients the character toward another character.
    /// </summary>
    public void FaceCharacter(Character target)
    {
        if (target == null) return;
        FaceTarget(target.transform.position);
    }

    /// <summary>
    /// Sets a persistent look target for orientation.
    /// The sprite will automatically orient toward this target each frame.
    /// </summary>
    public void SetLookTarget(Transform target)
    {
        _lookTarget = target;
    }

    /// <summary>
    /// Sets a Character as a persistent look target.
    /// </summary>
    public void SetLookTarget(Character target)
    {
        _lookTarget = target != null ? target.transform : null;
    }

    /// <summary>
    /// Clears the look target. Flip returns to being controlled by movement.
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
        // If a look target is active, movement does not control orientation
        if (_lookTarget != null) return;

        if (Mathf.Abs(moveDir.x) > 0.01f)
        {
            IsFacingRight = (moveDir.x > 0);
        }
    }

    /// <summary>
    /// Determines if the character is walking forward or backward relative to their orientation.
    /// Primarily used when a LookTarget is active.
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
                    // If X movement is in the same direction as orientation -> Forward
                    // Otherwise -> Backward
                    if (Mathf.Sign(moveX) == Mathf.Sign(facingDir))
                        isWalkingForward = true;
                    else
                        isWalkingBackward = true;
                }
                else
                {
                    // Purely vertical (Z) movement while looking at a target to the side
                    // Consider this as walking forward by default
                    isWalkingForward = true;
                }
            }
            else
            {
                // No target: normal forward walk
                isWalkingForward = true;
            }
        }

        _characterAnimator.SetWalkingForward(isWalkingForward);
        _characterAnimator.SetWalkingBackward(isWalkingBackward);
    }

    public bool HasALookTarget() => _lookTarget != null;

    private void ApplyFlip()
    {
        // Apply scale on visualRoot to avoid conflicts with NetworkTransform
        Transform target = visualRoot != null ? visualRoot : transform;
        Vector3 scale = target.localScale;
        scale.x = Mathf.Abs(scale.x) * (IsFacingRight ? 1 : -1);
        target.localScale = scale;
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



        // Preserve the sign of scale X to avoid breaking the current Flip

        float currentFlip = Mathf.Sign(target.localScale.x);

        target.localScale = new Vector3(scale * currentFlip, scale, scale);

    }



    // Color encapsulation for properties

    public Color SkinColor { get => skinColor; set { skinColor = value; ApplyColor(VisualPart.Skin, value); } }

    private Color skinColor;

    // ... (Repeat the pattern for other colors if needed)



    #endregion



    #region Helpers & Tooling



    public List<SpriteResolver> GetResolvers(ResolverPart part)

        => spriteResolvers.TryGetValue(part, out var list) ? list : new List<SpriteResolver>();



    // Call this at the end of SpawnCharacter
    public void RequestAutoResize()
    {
        if (_resizeCoroutine != null)
            StopCoroutine(_resizeCoroutine);

        _resizeCoroutine = StartCoroutine(ResizeRoutine());
    }

    private IEnumerator ResizeRoutine()
    {
        // Wait for Unity to finish calculating sprites and rendering
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

        // 1. Calculate visual bounds
        Bounds b = srs[0].bounds;
        foreach (var sr in srs) b.Encapsulate(sr.bounds);

        // 2. Reset local position
        // Force the visual to be exactly on the parent pivot (0,0,0)
        transform.localPosition = Vector3.zero;

        // 3. Calculate height
        // Measure the distance from the bottom to the top of the sprite
        float height = b.size.y;
        col.height = height;
        col.radius = 0.75f;

        // 4. Position the Collider
        // Place it so it starts at 0 and extends upward
        // No longer based on bounds for center, use pure height instead
        col.center = new Vector3(0, height / 2f, 0);

        Debug.Log("<color=white>[Visual]</color> Offset removed. Pivot forced to (0,0,0).");
    }

    public Vector3 GetVisualExtremity(Vector3 direction)
    {
        SpriteRenderer[] srs = GetComponentsInChildren<SpriteRenderer>(false);
        // Filter to exclude shadows and utility elements that would skew the actual body bounds
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


    #region ICharacterVisual Implementation

    /// <inheritdoc/>
    public event Action<string> OnAnimationEvent;

    /// <summary>
    /// Raises an animation event to all subscribers. Call from animation clips or event hooks.
    /// </summary>
    public void RaiseAnimationEvent(string eventName)
    {
        OnAnimationEvent?.Invoke(eventName);
    }

    void ICharacterVisual.Initialize(Character character, CharacterArchetype archetype)
    {
        // No-op for sprite visual: initialization is handled by existing Awake/OnNetworkSpawn lifecycle.
    }

    void ICharacterVisual.Cleanup()
    {
        // No-op for sprite visual: cleanup is handled by existing OnDestroy/OnNetworkDespawn lifecycle.
    }

    void ICharacterVisual.SetFacingDirection(float direction)
    {
        IsFacingRight = direction >= 0f;
    }

    void ICharacterVisual.PlayAnimation(AnimationKey key, bool loop)
    {
        if (_characterAnimator == null) return;

        switch (key)
        {
            case AnimationKey.Idle:
                _characterAnimator.StopLocomotion();
                break;
            case AnimationKey.Attack:
                _characterAnimator.PlayMeleeAttack();
                break;
            case AnimationKey.PickUp:
                _characterAnimator.PlayPickUpItem();
                break;
            case AnimationKey.Die:
                _characterAnimator.SetDead(true);
                break;
            // Walk, Run, GetHit, Action are handled via animator parameters in the existing system
            default:
                break;
        }
    }

    void ICharacterVisual.PlayAnimation(string customKey, bool loop)
    {
        Debug.LogWarning($"[CharacterVisual] Custom animation keys are not supported by sprite visual. Key: {customKey}");
    }

    bool ICharacterVisual.IsAnimationPlaying(AnimationKey key)
    {
        if (_characterAnimator == null) return false;

        if (key == AnimationKey.Die)
        {
            // Check if the animator is in the Dead state
            var animator = _characterAnimator.GetComponent<Animator>();
            if (animator != null)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                return stateInfo.IsName("Dead") || stateInfo.IsName("Die");
            }
        }

        // For other keys, the sprite animator system uses parameters rather than discrete states
        return false;
    }

    void ICharacterVisual.ConfigureCollider(Collider collider)
    {
        ResizeColliderToSprite();
    }

    void ICharacterVisual.SetHighlight(bool active)
    {
        // No-op for sprite visual. Will be implemented during Spine migration.
    }

    void ICharacterVisual.SetTint(Color color)
    {
        // Tech debt: direct sr.color modification. Will move to MPB in Spine migration.
        if (allRenderers == null) return;
        foreach (var sr in allRenderers)
        {
            if (sr != null) sr.color = color;
        }
    }

    void ICharacterVisual.SetVisible(bool visible)
    {
        if (allRenderers == null) return;
        foreach (var sr in allRenderers)
        {
            if (sr != null) sr.enabled = visible;
        }
    }

    #endregion


    #region IAnimationLayering Implementation

    void IAnimationLayering.PlayOverlayAnimation(AnimationKey key, int layer, bool loop)
    {
        // Sprite visual is single-layer; delegate to base PlayAnimation
        ((ICharacterVisual)this).PlayAnimation(key, loop);
    }

    void IAnimationLayering.PlayOverlayAnimation(string customKey, int layer, bool loop)
    {
        Debug.LogWarning($"[CharacterVisual] Overlay animations are not supported by sprite visual. Key: {customKey}, Layer: {layer}");
    }

    void IAnimationLayering.ClearOverlayAnimation(int layer)
    {
        // No-op: sprite visual does not have animation layers.
    }

    #endregion

}