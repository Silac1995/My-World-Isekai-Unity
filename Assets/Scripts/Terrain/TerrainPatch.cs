using UnityEngine;

namespace MWI.Terrain
{
    [RequireComponent(typeof(BoxCollider))]
    public class TerrainPatch : MonoBehaviour
    {
        [SerializeField] private TerrainType _baseTerrainType;
        [SerializeField] private float _baseFertility = 0.5f;
        [SerializeField] private int _priority = 0;

        public TerrainType BaseTerrainType => _baseTerrainType;
        public float BaseFertility => _baseFertility;
        public int Priority => _priority;

        private BoxCollider _collider;

        public Bounds Bounds
        {
            get
            {
                if (_collider == null) _collider = GetComponent<BoxCollider>();
                return _collider.bounds;
            }
        }

        private void Awake()
        {
            _collider = GetComponent<BoxCollider>();
            _collider.isTrigger = true;
        }
    }
}
