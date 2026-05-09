using UnityEngine;

/// <summary>
/// Blueprint defining what a character type is: capabilities, visuals, locomotion, AI defaults.
/// The archetype is data, not code. Runtime behavior is determined by the capability registry.
/// Archetype flags are for editor tooling and prefab validation only.
/// </summary>
[CreateAssetMenu(fileName = "New Character Archetype", menuName = "MWI/Character/Character Archetype")]
public class CharacterArchetype : ScriptableObject
{
    // ── Identity ──────────────────────────────────────────────────
    [Header("Identity")]
    [SerializeField] private string _archetypeName;
    [SerializeField] private BodyType _bodyType;
    [SerializeField] private FootSurfaceType _defaultFootSurface = FootSurfaceType.BareSkin;

    public string ArchetypeName => _archetypeName;
    public BodyType BodyType => _bodyType;
    public FootSurfaceType DefaultFootSurface => _defaultFootSurface;

    // ── Capability Flags (editor validation only) ─────────────────
    [Header("Capabilities (Validation Only — Registry is Runtime Truth)")]
    [SerializeField] private bool _canEnterCombat = true;
    [SerializeField] private bool _canEquipItems = true;
    [SerializeField] private bool _canDialogue = true;
    [SerializeField] private bool _canCraft = true;
    [SerializeField] private bool _hasInventory = true;
    [SerializeField] private bool _hasNeeds = true;
    [SerializeField] private bool _isTameable;
    [SerializeField] private bool _isMountable;

    public bool CanEnterCombat => _canEnterCombat;
    public bool CanEquipItems => _canEquipItems;
    public bool CanDialogue => _canDialogue;
    public bool CanCraft => _canCraft;
    public bool HasInventory => _hasInventory;
    public bool HasNeeds => _hasNeeds;
    public bool IsTameable => _isTameable;
    public bool IsMountable => _isMountable;

    [Header("Animal Behavior")]
    [Tooltip("0 = always tameable, 1 = untameable. Roll: UnityEngine.Random.value > TameDifficulty.")]
    [SerializeField, Range(0f, 1f)] private float _tameDifficulty = 0.5f;

    public float TameDifficulty => _tameDifficulty;

    // ── Locomotion ────────────────────────────────────────────────
    [Header("Locomotion")]
    [SerializeField] private MovementMode _movementModes = MovementMode.Walk | MovementMode.Run;
    [SerializeField] private float _defaultSpeed = 3.5f;
    [SerializeField] private float _runSpeed = 6f;

    public MovementMode MovementModes => _movementModes;
    public float DefaultSpeed => _defaultSpeed;
    public float RunSpeed => _runSpeed;

    // ── AI Defaults ───────────────────────────────────────────────
    [Header("AI Defaults")]
    [SerializeField] private WanderStyle _defaultWanderStyle = WanderStyle.Straight;
    [Tooltip("BT asset assigned to NPCBehaviourTree for this archetype's default behavior")]
    [SerializeField] private ScriptableObject _defaultBehaviourTree;

    public WanderStyle DefaultWanderStyle => _defaultWanderStyle;
    public ScriptableObject DefaultBehaviourTree => _defaultBehaviourTree;

    // ── Visual ────────────────────────────────────────────────────
    [Header("Visual")]
    [SerializeField] private AnimationProfile _animationProfile;
    [Tooltip("Prefab containing the visual child GO with ICharacterVisual implementation")]
    [SerializeField] private GameObject _visualPrefab;

    public AnimationProfile AnimationProfile => _animationProfile;
    public GameObject VisualPrefab => _visualPrefab;

    // ── Interaction ───────────────────────────────────────────────
    [Header("Interaction")]
    [SerializeField] private float _defaultInteractionRange = 3.5f;
    [SerializeField] private bool _isTargetable = true;

    public float DefaultInteractionRange => _defaultInteractionRange;
    public bool IsTargetable => _isTargetable;
}
