using UnityEngine;

namespace MWI.Farming
{
    /// <summary>Seed item — when held in active hand, pressing E starts crop placement. See spec §3.3.</summary>
    [CreateAssetMenu(menuName = "Game/Items/Seed")]
    public class SeedSO : MiscSO
    {
        [SerializeField] private CropSO _cropToPlant;
        public CropSO CropToPlant => _cropToPlant;
    }
}
