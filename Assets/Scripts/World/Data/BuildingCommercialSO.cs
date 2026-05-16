using UnityEngine;

namespace MWI.WorldSystem
{
    /// <summary>
    /// Commercial-flavoured BuildingSO blueprint. Adds the BaseTreasury seed amount
    /// that <see cref="CommercialBuilding.OnDefaultFurnitureSpawned"/> credits into
    /// the building's Treasury-role SafeFurniture at construction-complete time.
    /// Currency is resolved at credit time from the enclosing MapController's
    /// NativeCurrency (or CurrencyId.Default when there is no enclosing map).
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingCommercialSO", menuName = "MWI/World/BuildingCommercialSO", order = 101)]
    public class BuildingCommercialSO : BuildingSO
    {
        [Header("Commercial — Treasury Seed")]
        [Tooltip("Amount credited into the Treasury safe ONCE at construction-complete. Currency is resolved at that moment from CommunityData.NativeCurrency (or CurrencyId.Default when no enclosing community exists). Persisted via BuildingSaveData.TreasurySeeded so a save/reload does not re-credit.")]
        [Min(0)]
        [SerializeField] private int _baseTreasury;

        public int BaseTreasury => _baseTreasury;
    }
}
