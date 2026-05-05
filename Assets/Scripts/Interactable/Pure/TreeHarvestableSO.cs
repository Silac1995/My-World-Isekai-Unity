using UnityEngine;

namespace MWI.Interactables
{
    /// <summary>
    /// Tree-flavoured <see cref="HarvestableSO"/>. Adds the authoring surface for the
    /// 3-layer visual driven by <c>HarvestableLayeredVisual</c>:
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
    /// Lives in the <c>MWI.Interactable.Pure</c> asmdef alongside <see cref="HarvestableSO"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Harvestables/Tree")]
    public class TreeHarvestableSO : HarvestableSO
    {
        [Header("Layered tree visual")]
        [SerializeField] private Sprite _trunkSprite;
        [SerializeField] private Sprite _foliageSprite;
        [SerializeField] private Gradient _foliageColorOverYear = new Gradient();
        [SerializeField] private Sprite[] _fruitSpriteVariants = new Sprite[0];
        [Tooltip("Local-space rect (in the foliage Transform's frame) where fruits may spawn. Leave at Rect.zero to use the foliage sprite's own bounds.")]
        [SerializeField] private Rect _fruitSpawnArea = Rect.zero;
        [SerializeField] private Vector2 _fruitScale = Vector2.one;

        public Sprite TrunkSprite => _trunkSprite;
        public Sprite FoliageSprite => _foliageSprite;
        public Gradient FoliageColorOverYear => _foliageColorOverYear;
        public Sprite[] FruitSpriteVariants => _fruitSpriteVariants;
        public Rect FruitSpawnArea => _fruitSpawnArea;
        public Vector2 FruitScale => _fruitScale;
    }
}
