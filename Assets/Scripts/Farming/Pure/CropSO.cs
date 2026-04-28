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
        [SerializeField] private ScriptableObject _produceItem;
        [SerializeField] private int _produceCount = 1;
        [SerializeField] private ScriptableObject _requiredHarvestTool;
        [SerializeField] private Sprite[] _stageSprites;
        [SerializeField] private GameObject _harvestablePrefab;

        [Header("Perennial (apple tree, berry bush)")]
        [SerializeField] private bool _isPerennial;
        [SerializeField] private int _regrowDays = 3;

        [Header("Destruction (axe / pickaxe etc.)")]
        [SerializeField] private bool _allowDestruction;
        [SerializeField] private ScriptableObject _requiredDestructionTool;
        [SerializeField] private List<ScriptableObject> _destructionOutputs = new List<ScriptableObject>();
        [SerializeField] private int _destructionOutputCount = 1;
        [SerializeField] private float _destructionDuration = 3f;

        public string Id => _id;
        public string DisplayName => _displayName;
        public int DaysToMature => _daysToMature;
        public float MinMoistureForGrowth => _minMoistureForGrowth;
        public float PlantDuration => _plantDuration;
        public ScriptableObject ProduceItem => _produceItem;
        public int ProduceCount => _produceCount;
        public ScriptableObject RequiredHarvestTool => _requiredHarvestTool;
        public bool IsPerennial => _isPerennial;
        public int RegrowDays => _regrowDays;
        public bool AllowDestruction => _allowDestruction;
        public ScriptableObject RequiredDestructionTool => _requiredDestructionTool;
        public IReadOnlyList<ScriptableObject> DestructionOutputs => _destructionOutputs;
        public int DestructionOutputCount => _destructionOutputCount;
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

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(_id))
                Debug.LogWarning($"[CropSO] {name}: _id is empty. The cell uses Id as the persistence key — set this field.");
            if (_produceItem == null)
                Debug.LogWarning($"[CropSO] {name}: _produceItem is null.");
            if (_stageSprites != null && _stageSprites.Length != _daysToMature)
                Debug.LogWarning($"[CropSO] {name}: _stageSprites.Length ({_stageSprites.Length}) should equal _daysToMature ({_daysToMature}). The mature visual lives on CropHarvestable._readySprite, not in _stageSprites.");
            if (_isPerennial && (_regrowDays < 1 || _regrowDays > _daysToMature))
                Debug.LogWarning($"[CropSO] {name}: perennial _regrowDays must be in [1, _daysToMature].");
        }
#endif
    }
}
