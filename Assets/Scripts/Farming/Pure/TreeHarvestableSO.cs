using UnityEngine;
using MWI.Interactables;

namespace MWI.Farming
{
    /// <summary>
    /// Tree-flavoured <see cref="CropSO"/>. Inherits the full farming pipeline (DaysToMature,
    /// MinMoistureForGrowth, IsPerennial, RegrowDays, PlantDuration, StageSprites) plus the
    /// universal <see cref="HarvestableSO"/> surface (yield outputs, depletion, destruction,
    /// tools, prefab) AND adds the authoring surface for the 3-layer visual driven by
    /// <c>HarvestableLayeredVisual</c>:
    ///
    /// <list type="bullet">
    /// <item><see cref="TrunkSprite"/> — static silhouette under the foliage.</item>
    /// <item><see cref="FoliageSprite"/> — single sprite, MPB-tinted by <see cref="FoliageColorOverYear"/>
    ///       sampled at <c>TimeManager.CurrentYearProgress01</c>.</item>
    /// <item><see cref="FruitSpriteVariants"/> — random pick per spawned fruit. Empty = no fruit.</item>
    /// <item><see cref="FruitSpawnArea"/> — local-space rect (in foliage frame) where fruits may
    ///       spawn. <see cref="Rect.zero"/> = use the foliage sprite's bounds.</item>
    /// <item><see cref="FruitScale"/> — per-fruit scale multiplier.</item>
    /// </list>
    ///
    /// Lives in the <c>MWI.Farming.Pure</c> asmdef (next to <see cref="CropSO"/>) so it can
    /// extend the farming inheritance chain. Use this SO whenever a tree-shaped crop or
    /// scene-placed tree wants the layered visual treatment. For non-tree crops (wheat,
    /// flowers) keep the plain <see cref="CropSO"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Farming/Tree Crop")]
    public class TreeHarvestableSO : CropSO
    {
        [Header("Layered tree visual")]
        [SerializeField] private Sprite _trunkSprite;
        [SerializeField] private Sprite _foliageSprite;
        [SerializeField] private Gradient _foliageColorOverYear = new Gradient();
        [SerializeField] private Sprite[] _fruitSpriteVariants = new Sprite[0];
        [Tooltip("Local-space rect (in the foliage Transform's frame) where fruits may spawn. Leave at Rect.zero to use the foliage sprite's own bounds.")]
        [SerializeField] private Rect _fruitSpawnArea = Rect.zero;
        [SerializeField] private Vector2 _fruitScale = Vector2.one;
        [Tooltip("Fractional inset toward the foliage center applied to every fruit position so a fruit sprite anchored at the leaf-mesh boundary doesn't extend visually past the silhouette. 0 = no inset (sample reaches the literal mesh edge); 0.1 = 10% pull toward sprite center (sensible default for large foliage sprites with smooth outlines); 0.5 = collapsed to center. Clamped to [0, 0.5).")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _fruitPadding = 0.1f;

        public Sprite TrunkSprite => _trunkSprite;
        public Sprite FoliageSprite => _foliageSprite;
        public Gradient FoliageColorOverYear => _foliageColorOverYear;
        public Sprite[] FruitSpriteVariants => _fruitSpriteVariants;
        public Rect FruitSpawnArea => _fruitSpawnArea;
        public Vector2 FruitScale => _fruitScale;
        /// <summary>Fractional inset toward the foliage center applied to every fruit position
        /// (mesh + rect sampling paths). Keeps fruit sprites from visually overhanging the leaf
        /// silhouette. Clamped to [0, 0.5).</summary>
        public float FruitPadding => Mathf.Clamp(_fruitPadding, 0f, 0.499f);
    }
}
