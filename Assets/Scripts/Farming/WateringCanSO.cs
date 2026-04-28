using UnityEngine;

namespace MWI.Farming
{
    /// <summary>Watering can — when held, pressing E starts watering mode. See spec §3.4.</summary>
    [CreateAssetMenu(menuName = "Game/Items/WateringCan")]
    public class WateringCanSO : MiscSO
    {
        [SerializeField] private float _moistureSetTo = 1f;
        public float MoistureSetTo => _moistureSetTo;
    }
}
