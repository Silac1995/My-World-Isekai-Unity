using System.Collections.Generic;
using UnityEngine;
using MWI.Interactables;

namespace MWI.Farming
{
    /// <summary>
    /// Content definition for one crop type. Specialises <see cref="HarvestableSO"/> with
    /// farming-specific fields: time-to-mature, moisture gating, plant action duration,
    /// perennial refill cycle, growing-stage sprites. Loaded into <see cref="CropRegistry"/>
    /// at game launch from <c>Resources/Data/Farming/Crops</c>. See farming spec §3.1.
    /// </summary>
    /// <remarks>
    /// Inheriting from <see cref="HarvestableSO"/> means the universal harvestable fields
    /// (yield outputs, destruction outputs, tool gates, depletion config, ready/depleted
    /// sprites, runtime prefab) live on the base — every consumer of <c>HarvestableSO</c>
    /// (<c>Harvestable</c>, future <c>OreNodeSO</c>, designer tooling) sees them uniformly.
    /// Existing <c>CropSO</c> assets on disk keep working: Unity's serialiser walks the type
    /// hierarchy when deserialising, so YAML fields like <c>_id:</c>, <c>_harvestOutputs:</c>,
    /// <c>_destructionEntries:</c>, etc. land on the inherited base fields by name match.
    /// Item-typed fields stay <see cref="ScriptableObject"/> for the same Pure-asmdef
    /// reason as before.
    /// </remarks>
    [CreateAssetMenu(menuName = "Game/Farming/Crop")]
    public class CropSO : HarvestableSO
    {
        [Header("Farming-specific")]
        [SerializeField] private int _daysToMature = 4;
        [SerializeField] private float _minMoistureForGrowth = 0.3f;
        [SerializeField] private float _plantDuration = 1f;
        [SerializeField] private Sprite[] _stageSprites;

        [Header("Perennial (apple tree, berry bush)")]
        [SerializeField] private bool _isPerennial;
        [SerializeField] private int _regrowDays = 3;

        // ────── Legacy fields kept for one-shot OnValidate migration of pre-rework assets.
        // Pre-rework CropSO had `_produceItem` + `_produceCount` (single ItemSO + flat int)
        // and `_destructionOutputs` + `_destructionOutputCount` (List<ItemSO> + flat int).
        // The current schema uses (Item, Count) entry lists on the HarvestableSO base.
        // These fields keep the OLD names so existing serialised assets load their data
        // cleanly; OnValidate then migrates them into the base's _harvestOutputs /
        // _destructionEntries (via SetHarvestOutputsForTests) and clears them. Runtime code
        // never reads these. Safe to delete after every asset has been re-saved at least once.
        [SerializeField, HideInInspector] private ScriptableObject _produceItem;
        [SerializeField, HideInInspector] private int _produceCount = 1;
        [SerializeField, HideInInspector] private List<ScriptableObject> _destructionOutputs = new List<ScriptableObject>();
        [SerializeField, HideInInspector] private int _destructionOutputCount = 1;

        public int DaysToMature => _daysToMature;
        public float MinMoistureForGrowth => _minMoistureForGrowth;
        public float PlantDuration => _plantDuration;
        public bool IsPerennial => _isPerennial;
        public int RegrowDays => _regrowDays;

        // Caller must guard against growthTimer >= DaysToMature (mature visual lives on the
        // base ReadySprite). Clamp here is defensive only.
        public Sprite GetStageSprite(int growthTimer)
        {
            if (_stageSprites == null || _stageSprites.Length == 0) return null;
            return _stageSprites[Mathf.Clamp(growthTimer, 0, _stageSprites.Length - 1)];
        }

#if UNITY_EDITOR
        public void SetDaysToMatureForTests(int days) => _daysToMature = days;
        public void SetMinMoistureForTests(float m) => _minMoistureForGrowth = m;
        public void SetIsPerennialForTests(bool p) => _isPerennial = p;
        public void SetRegrowDaysForTests(int d) => _regrowDays = d;

        private void OnValidate()
        {
            // One-shot migration for pre-rework assets: copy legacy single-item fields into
            // the base's entry lists, then null out the legacy slots so a subsequent save
            // strips them on disk. Reads and writes go through the base's editor-only
            // setters because the base fields are private.
            if (HarvestOutputs.Count == 0 && _produceItem != null)
            {
                var migrated = new List<HarvestableOutputEntry>
                {
                    new HarvestableOutputEntry { Item = _produceItem, Count = Mathf.Max(1, _produceCount) }
                };
                SetHarvestOutputsForTests(migrated);
                _produceItem = null;
                _produceCount = 1;
            }
            if (DestructionOutputs.Count == 0 && _destructionOutputs != null && _destructionOutputs.Count > 0)
            {
                int count = Mathf.Max(1, _destructionOutputCount);
                var migrated = new List<HarvestableOutputEntry>(_destructionOutputs.Count);
                for (int i = 0; i < _destructionOutputs.Count; i++)
                {
                    var item = _destructionOutputs[i];
                    if (item == null) continue;
                    migrated.Add(new HarvestableOutputEntry { Item = item, Count = count });
                }
                SetDestructionOutputsForTests(migrated);
                _destructionOutputs.Clear();
                _destructionOutputCount = 1;
            }

            if (string.IsNullOrEmpty(Id))
                Debug.LogWarning($"[CropSO] {name}: _id is empty. The cell uses Id as the persistence key — set this field.");
            if (HarvestOutputs.Count == 0)
                Debug.LogWarning($"[CropSO] {name}: _harvestOutputs is empty — the crop will not produce anything on harvest.");
            if (_stageSprites != null && _stageSprites.Length != _daysToMature)
                Debug.LogWarning($"[CropSO] {name}: _stageSprites.Length ({_stageSprites.Length}) should equal _daysToMature ({_daysToMature}). The mature visual lives on the base ReadySprite, not in _stageSprites.");
            if (_isPerennial && (_regrowDays < 1 || _regrowDays > _daysToMature))
                Debug.LogWarning($"[CropSO] {name}: perennial _regrowDays must be in [1, _daysToMature].");
        }
#endif
    }
}
