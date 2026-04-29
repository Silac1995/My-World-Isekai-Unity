using System.Collections.Generic;
using UnityEngine;

namespace MWI.Interactables
{
    /// <summary>
    /// Universal data root for any resource node — wild trees, scene-authored ore veins,
    /// player-planted crops, dungeon mineral deposits, etc. Subclasses extend this with
    /// content-specific fields (e.g. <see cref="MWI.Farming.CropSO"/> adds growth duration,
    /// moisture threshold, seed link, season flags).
    ///
    /// Lives in the <c>MWI.Interactable.Pure</c> asmdef so it can be referenced from other
    /// Pure asmdefs (notably <c>MWI.Farming.Pure</c> where <c>CropSO : HarvestableSO</c>) AND
    /// from Assembly-CSharp where <c>Harvestable</c> consumes it via the optional
    /// <c>_so</c> field. Same constraint as <see cref="CropSO"/>: item-typed fields are
    /// declared as <see cref="ScriptableObject"/> rather than <c>ItemSO</c> because Pure
    /// asmdefs cannot reference Assembly-CSharp. Consumers cast back to <c>ItemSO</c> at
    /// use sites.
    /// </summary>
    public class HarvestableSO : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;

        [Header("Yield (the default 'pick' interaction)")]
        [SerializeField] private List<HarvestableOutputEntry> _harvestOutputs = new List<HarvestableOutputEntry>();
        [Tooltip("Null = bare hands (or any held item) work for the yield path.")]
        [SerializeField] private ScriptableObject _requiredHarvestTool;
        [SerializeField] private float _harvestDuration = 3f;

        [Header("Depletion / Respawn")]
        [SerializeField] private bool _isDepletable = true;
        [SerializeField] private int _maxHarvestCount = 5;
        [Tooltip("Days before the resource auto-respawns. Subclasses with their own refill cycle (e.g. CropSO perennials via FarmGrowthSystem) may ignore this — they override Harvestable.ScheduleRespawnAfterDeplete.")]
        [SerializeField] private int _respawnDelayDays = 1;

        [Header("Destruction (axe / pickaxe etc.)")]
        [SerializeField] private bool _allowDestruction;
        [Tooltip("When true (default), NPCs (HarvestingBuilding workers) may autonomously destroy this harvestable to obtain its destruction outputs. Set to false to protect a node from autonomous NPC consumption (the player's Hold-E → Destroy menu still works regardless). AllowDestruction must also be true for either path to fire.")]
        [SerializeField] private bool _allowNpcDestruction = true;
        [SerializeField] private ScriptableObject _requiredDestructionTool;
        // Field is named _destructionEntries (not _destructionOutputs) because CropSO
        // subclasses keep a legacy `_destructionOutputs : List<ScriptableObject>` field
        // for one-shot migration of pre-2026-04-29 crop assets, and Unity's serialiser
        // forbids the same field name appearing on both base and derived class.
        [SerializeField] private List<HarvestableOutputEntry> _destructionEntries = new List<HarvestableOutputEntry>();
        [SerializeField] private float _destructionDuration = 3f;

        [Header("Visuals")]
        [SerializeField] private Sprite _readySprite;
        [SerializeField] private Sprite _depletedSprite;

        [Header("Spawn")]
        [Tooltip("Prefab to instantiate when this harvestable is spawned at runtime (planted crop, dynamic ore vein, …). Optional — wild scene-authored harvestables don't need this.")]
        [SerializeField] private GameObject _harvestablePrefab;

        // ── Read-only accessors ──────────────────────────────────────────

        public string Id => _id;
        public string DisplayName => _displayName;

        public IReadOnlyList<HarvestableOutputEntry> HarvestOutputs => _harvestOutputs;
        public ScriptableObject RequiredHarvestTool => _requiredHarvestTool;
        public float HarvestDuration => _harvestDuration;

        public bool IsDepletable => _isDepletable;
        public int MaxHarvestCount => _maxHarvestCount;
        public int RespawnDelayDays => _respawnDelayDays;

        public bool AllowDestruction => _allowDestruction;
        public bool AllowNpcDestruction => _allowNpcDestruction;
        public ScriptableObject RequiredDestructionTool => _requiredDestructionTool;
        public IReadOnlyList<HarvestableOutputEntry> DestructionOutputs => _destructionEntries;
        public float DestructionDuration => _destructionDuration;

        public Sprite ReadySprite => _readySprite;
        public Sprite DepletedSprite => _depletedSprite;

        public GameObject HarvestablePrefab => _harvestablePrefab;

#if UNITY_EDITOR
        // Editor-only setters used by tests + the OnValidate migrations in subclasses.
        // Direct field access stays private so designer-side authoring goes through the
        // Inspector, which is the supported path.
        public void SetIdForTests(string id) => _id = id;
        public void SetHarvestOutputsForTests(List<HarvestableOutputEntry> entries)
            => _harvestOutputs = entries ?? new List<HarvestableOutputEntry>();
        public void SetDestructionOutputsForTests(List<HarvestableOutputEntry> entries)
            => _destructionEntries = entries ?? new List<HarvestableOutputEntry>();
        public void SetAllowDestructionForTests(bool b) => _allowDestruction = b;
        public void SetMaxHarvestCountForTests(int n) => _maxHarvestCount = n;
        public void SetIsDepletableForTests(bool b) => _isDepletable = b;
        public void SetRespawnDelayDaysForTests(int d) => _respawnDelayDays = d;
#endif
    }
}
