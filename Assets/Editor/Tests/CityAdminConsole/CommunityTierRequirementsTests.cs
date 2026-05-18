using NUnit.Framework;
using MWI.WorldSystem;

namespace MWI.Tests.CityAdminConsole
{
    /// <summary>
    /// Contract tests for <see cref="CommunityTierRegistry"/> + the 7 bootstrap
    /// <see cref="CommunityTierRequirementsSO"/> assets in Resources/Data/CommunityTiers/.
    ///
    /// Plan 4c Task 2.
    /// </summary>
    public class CommunityTierRequirementsTests
    {
        [SetUp]
        public void Setup()
        {
            // Each test starts cold to exercise the lazy-init path.
            CommunityTierRegistry.ResetForTests();
        }

        [Test]
        public void Registry_GetSmallGroup_ReturnsNonNull()
        {
            var so = CommunityTierRegistry.Get(CommunityLevel.SmallGroup);
            Assert.IsNotNull(so, "SmallGroup tier asset must exist in Resources/Data/CommunityTiers/.");
            Assert.AreEqual(CommunityLevel.SmallGroup, so.Level);
        }

        [Test]
        public void Registry_GetCamp_HasReasonableMinPop()
        {
            var so = CommunityTierRegistry.Get(CommunityLevel.Camp);
            Assert.IsNotNull(so);
            Assert.GreaterOrEqual(so.MinPopulation, 1, "Camp tier expects at least 1 citizen.");
        }

        [Test]
        public void Registry_GetEmpire_ReturnsNonNull()
        {
            var so = CommunityTierRegistry.Get(CommunityLevel.Empire);
            Assert.IsNotNull(so);
            Assert.AreEqual(CommunityLevel.Empire, so.Level);
        }

        [Test]
        public void Registry_MinPopulation_IncreasesMonotonicallyAcrossTiers()
        {
            // SmallGroup ≤ Camp ≤ Village ≤ Town ≤ City ≤ Kingdom ≤ Empire
            var levels = new[]
            {
                CommunityLevel.SmallGroup,
                CommunityLevel.Camp,
                CommunityLevel.Village,
                CommunityLevel.Town,
                CommunityLevel.City,
                CommunityLevel.Kingdom,
                CommunityLevel.Empire,
            };

            int prev = -1;
            for (int i = 0; i < levels.Length; i++)
            {
                var so = CommunityTierRegistry.Get(levels[i]);
                Assert.IsNotNull(so, $"Missing tier asset for {levels[i]}.");
                Assert.GreaterOrEqual(so.MinPopulation, prev,
                    $"Tier {levels[i]} has lower MinPopulation than previous tier — monotonicity violated.");
                prev = so.MinPopulation;
            }
        }

        [Test]
        public void Registry_MinTreasury_IncreasesMonotonicallyAcrossTiers()
        {
            var levels = new[]
            {
                CommunityLevel.SmallGroup,
                CommunityLevel.Camp,
                CommunityLevel.Village,
                CommunityLevel.Town,
                CommunityLevel.City,
                CommunityLevel.Kingdom,
                CommunityLevel.Empire,
            };

            int prev = -1;
            for (int i = 0; i < levels.Length; i++)
            {
                var so = CommunityTierRegistry.Get(levels[i]);
                Assert.IsNotNull(so);
                Assert.GreaterOrEqual(so.MinTreasury, prev,
                    $"Tier {levels[i]} has lower MinTreasury than previous — monotonicity violated.");
                prev = so.MinTreasury;
            }
        }

        [Test]
        public void Registry_LazyInit_IdempotentAcrossMultipleGets()
        {
            var first  = CommunityTierRegistry.Get(CommunityLevel.SmallGroup);
            var second = CommunityTierRegistry.Get(CommunityLevel.SmallGroup);
            Assert.AreSame(first, second, "Repeated Get() returns the same cached SO instance.");
        }

        [Test]
        public void Registry_ResetForTests_ForcesRescan()
        {
            var first = CommunityTierRegistry.Get(CommunityLevel.SmallGroup);
            Assert.IsNotNull(first);
            CommunityTierRegistry.ResetForTests();
            var second = CommunityTierRegistry.Get(CommunityLevel.SmallGroup);
            Assert.IsNotNull(second);
            // After reset the registry rebuilt — same underlying asset, same reference.
            Assert.AreSame(first, second);
        }

        [Test]
        public void Registry_GetForNextLevelFrom_SmallGroup_ReturnsCamp()
        {
            var next = CommunityTierRegistry.GetForNextLevelFrom(CommunityLevel.SmallGroup);
            Assert.IsNotNull(next);
            Assert.AreEqual(CommunityLevel.Camp, next.Level);
        }

        [Test]
        public void Registry_GetForNextLevelFrom_Empire_ReturnsNull()
        {
            // Empire is the max tier — no level above.
            var next = CommunityTierRegistry.GetForNextLevelFrom(CommunityLevel.Empire);
            Assert.IsNull(next, "Empire is the max tier; no requirements for the level above.");
        }
    }
}
