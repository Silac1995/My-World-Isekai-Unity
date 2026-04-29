using System.Collections.Generic;
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Content definition for one crop type. See farming spec §3.1.
    /// Loaded into <see cref="CropRegistry"/> at game launch from Resources/Data/Farming/Crops.
    /// </summary>
    /// <remarks>
    /// Item-typed fields (_produceItem, _requiredHarvestTool, _requiredDestructionTool,
    /// _destructionOutputs) are declared as <see cref="ScriptableObject"/> here because this
    /// type lives in the MWI.Farming.Pure asmdef which cannot reference Assembly-CSharp where
    /// ItemSO lives (matches the Hunger.Pure / Wages.Pure / Orders.Pure project pattern).
    /// Consumers in gameplay code cast to ItemSO at use sites.
    /// </remarks>
    [CreateAssetMenu(menuName = "Game/Farming/Crop")]
    public class CropSO : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private int _daysToMature = 4;
        [SerializeField] private float _minMoistureForGrowth = 0.3f;
        [SerializeField] private float _plantDuration = 1f;
        [SerializeField] private List<CropHarvestOutput> _harvestOutputs = new List<CropHarvestOutput>();
        [SerializeField] private ScriptableObject _requiredHarvestTool;
        [SerializeField] private Sprite[] _stageSprites;
        [SerializeField] private GameObject _harvestablePrefab;

        [Header("Perennial (apple tree, berry bush)")]
        [SerializeField] private bool _isPerennial;
        [SerializeField] private int _regrowDays = 3;

        [Header("Destruction (axe / pickaxe etc.)")]
        [SerializeField] private bool _allowDestruction;
        [SerializeField] private ScriptableObject _requiredDestructionTool;
        [SerializeField] private List<CropHarvestOutput> _destructionEntries = new List<CropHarvestOutput>();
        [SerializeField] private float _destructionDuration = 3f;

        // ────── Legacy fields kept for one-shot OnValidate migration of pre-rework assets.
        // Pre-rework CropSO had `_produceItem` + `_produceCount` (single ItemSO + flat int)
        // and `_destructionOutputs` + `_destructionOutputCount` (List<ItemSO> + flat int).
        // The new schema uses (Item, Count) entry lists. These fields keep the OLD names so
        // existing serialized assets load their data cleanly; OnValidate then migrates them
        // into _harvestOutputs / _destructionEntries and clears them. Runtime code never
        // reads these. Safe to delete after every asset has been re-saved at least once.
        [SerializeField, HideInInspector] private ScriptableObject _produceItem;
        [SerializeField, HideInInspector] private int _produceCount = 1;
        [SerializeField, HideInInspector] private List<ScriptableObject> _destructionOutputs = new List<ScriptableObject>();
        [SerializeField, HideInInspector] private int _destructionOutputCount = 1;

        public string Id => _id;
        public string DisplayName => _displayName;
        public int DaysToMature => _daysToMature;
        public float MinMoistureForGrowth => _minMoistureForGrowth;
        public float PlantDuration => _plantDuration;
        public IReadOnlyList<CropHarvestOutput> HarvestOutputs
        {
            get
            {
                // Lazy migration safety net: in the editor, OnValidate copies legacy
                // (_produceItem, _produceCount) into _harvestOutputs and clears legacy. But
                // OnValidate is editor-only — a built client whose asset bundle was packaged
                // before the migration ran on disk will still have the legacy fields populated
                // and _harvestOutputs empty. Self-heal here so runtime callers (CanHarvest,
                // GetInteractionOptions, CropHarvestable.InitializeFromCell) always see the
                // entry list. The legacy slot is NOT cleared here — the asset is read-only at
                // runtime in a build, and editor-side OnValidate handles persistent cleanup.
                if (_harvestOutputs.Count == 0 && _produceItem != null)
                    _harvestOutputs.Add(new CropHarvestOutput { Item = _produceItem, Count = Mathf.Max(1, _produceCount) });
                return _harvestOutputs;
            }
        }
        public ScriptableObject RequiredHarvestTool => _requiredHarvestTool;
        public bool IsPerennial => _isPerennial;
        public int RegrowDays => _regrowDays;
        public bool AllowDestruction => _allowDestruction;
        public ScriptableObject RequiredDestructionTool => _requiredDestructionTool;
        public IReadOnlyList<CropHarvestOutput> DestructionOutputs
        {
            get
            {
                // Same lazy migration as HarvestOutputs above.
                if (_destructionEntries.Count == 0 && _destructionOutputs != null && _destructionOutputs.Count > 0)
                {
                    int count = Mathf.Max(1, _destructionOutputCount);
                    for (int i = 0; i < _destructionOutputs.Count; i++)
                    {
                        var item = _destructionOutputs[i];
                        if (item == null) continue;
                        _destructionEntries.Add(new CropHarvestOutput { Item = item, Count = count });
                    }
                }
                return _destructionEntries;
            }
        }
        public float DestructionDuration => _destructionDuration;
        public GameObject HarvestablePrefab => _harvestablePrefab;

        // Caller must guard against growthTimer >= DaysToMature (mature visual lives on
        // CropHarvestable._readySprite). Clamp here is defensive only.
        public Sprite GetStageSprite(int growthTimer)
        {
            if (_stageSprites == null || _stageSprites.Length == 0) return null;
            return _stageSprites[Mathf.Clamp(growthTimer, 0, _stageSprites.Length - 1)];
        }

#if UNITY_EDITOR
        public void SetIdForTests(string id) => _id = id;
        public void SetDaysToMatureForTests(int days) => _daysToMature = days;
        public void SetMinMoistureForTests(float m) => _minMoistureForGrowth = m;
        public void SetIsPerennialForTests(bool p) => _isPerennial = p;
        public void SetRegrowDaysForTests(int d) => _regrowDays = d;

        public void SetHarvestOutputsForTests(List<CropHarvestOutput> entries)
            => _harvestOutputs = entries ?? new List<CropHarvestOutput>();
        public void SetDestructionOutputsForTests(List<CropHarvestOutput> entries)
            => _destructionEntries = entries ?? new List<CropHarvestOutput>();

        private void OnValidate()
        {
            // One-shot migration for pre-rework assets: copy legacy single-item fields into
            // the new entry lists, then null out the legacy slots so a subsequent save
            // strips them on disk.
            if (_harvestOutputs.Count == 0 && _produceItem != null)
            {
                _harvestOutputs.Add(new CropHarvestOutput { Item = _produceItem, Count = Mathf.Max(1, _produceCount) });
                _produceItem = null;
                _produceCount = 1;
            }
            if (_destructionEntries.Count == 0 && _destructionOutputs != null && _destructionOutputs.Count > 0)
            {
                int count = Mathf.Max(1, _destructionOutputCount);
                for (int i = 0; i < _destructionOutputs.Count; i++)
                {
                    var item = _destructionOutputs[i];
                    if (item == null) continue;
                    _destructionEntries.Add(new CropHarvestOutput { Item = item, Count = count });
                }
                _destructionOutputs.Clear();
                _destructionOutputCount = 1;
            }

            if (string.IsNullOrEmpty(_id))
                Debug.LogWarning($"[CropSO] {name}: _id is empty. The cell uses Id as the persistence key — set this field.");
            if (_harvestOutputs.Count == 0)
                Debug.LogWarning($"[CropSO] {name}: _harvestOutputs is empty — the crop will not produce anything on harvest.");
            if (_stageSprites != null && _stageSprites.Length != _daysToMature)
                Debug.LogWarning($"[CropSO] {name}: _stageSprites.Length ({_stageSprites.Length}) should equal _daysToMature ({_daysToMature}). The mature visual lives on CropHarvestable._readySprite, not in _stageSprites.");
            if (_isPerennial && (_regrowDays < 1 || _regrowDays > _daysToMature))
                Debug.LogWarning($"[CropSO] {name}: perennial _regrowDays must be in [1, _daysToMature].");
        }
#endif
    }
}
