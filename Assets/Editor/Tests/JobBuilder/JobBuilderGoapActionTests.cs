using NUnit.Framework;

// Plan 4b Task 3 — contract tests for the 4 NEW JobBuilder GoapActions.
namespace MWI.Tests.JobBuilder
{
    /// <summary>
    /// Contract tests for the four NEW GoapActions added in Plan 4b:
    /// TakeMaterialFromABStorage, GoToConstructionSite, DropMaterialAtZone,
    /// FinishBuildingConstruction.
    ///
    /// EditMode-only — these tests verify the dictionary contracts (Preconditions,
    /// Effects, Cost, ActionName, IsComplete initial state). Full integration is
    /// validated via the planner test in Plan 4c PlayMode-MP — instantiating Unity
    /// scenes for the IsValid/Execute paths is too physics-heavy for unit scope.
    /// </summary>
    public class JobBuilderGoapActionTests
    {
        // ── GoapAction_TakeMaterialFromABStorage ─────────────────────────

        [Test]
        public void TakeMaterialFromABStorage_Preconditions_RequireMissingHandAndStorage()
        {
            var action = new GoapAction_TakeMaterialFromABStorage(null);

            Assert.IsTrue(action.Preconditions.ContainsKey("hasMaterialsInHand"));
            Assert.IsFalse(action.Preconditions["hasMaterialsInHand"]);
            Assert.IsTrue(action.Preconditions.ContainsKey("hasActiveBuildOrder"));
            Assert.IsTrue(action.Preconditions["hasActiveBuildOrder"]);
            Assert.IsTrue(action.Preconditions.ContainsKey("hasMatchingMaterialInABStorage"));
            Assert.IsTrue(action.Preconditions["hasMatchingMaterialInABStorage"]);
        }

        [Test]
        public void TakeMaterialFromABStorage_Effects_SetHasMaterialsInHand()
        {
            var action = new GoapAction_TakeMaterialFromABStorage(null);
            Assert.IsTrue(action.Effects.ContainsKey("hasMaterialsInHand"));
            Assert.IsTrue(action.Effects["hasMaterialsInHand"]);
        }

        [Test]
        public void TakeMaterialFromABStorage_Cost_IsOne()
        {
            var action = new GoapAction_TakeMaterialFromABStorage(null);
            Assert.AreEqual(1f, action.Cost);
        }

        [Test]
        public void TakeMaterialFromABStorage_ActionName_IsExpected()
        {
            var action = new GoapAction_TakeMaterialFromABStorage(null);
            Assert.AreEqual("TakeMaterialFromABStorage", action.ActionName);
        }

        [Test]
        public void TakeMaterialFromABStorage_IsValid_FalseWithNullAB()
        {
            var action = new GoapAction_TakeMaterialFromABStorage(null);
            Assert.IsFalse(action.IsValid(null));
        }

        // ── GoapAction_GoToConstructionSite ──────────────────────────────

        [Test]
        public void GoToConstructionSite_Preconditions_RequireMaterialsInHandAndActiveOrder()
        {
            var action = new GoapAction_GoToConstructionSite(null);

            Assert.IsTrue(action.Preconditions.ContainsKey("hasMaterialsInHand"));
            Assert.IsTrue(action.Preconditions["hasMaterialsInHand"]);
            Assert.IsTrue(action.Preconditions.ContainsKey("hasActiveBuildOrder"));
            Assert.IsTrue(action.Preconditions["hasActiveBuildOrder"]);
        }

        [Test]
        public void GoToConstructionSite_Effects_SetInsideConstructionSite()
        {
            var action = new GoapAction_GoToConstructionSite(null);
            Assert.IsTrue(action.Effects.ContainsKey("insideConstructionSite"));
            Assert.IsTrue(action.Effects["insideConstructionSite"]);
        }

        [Test]
        public void GoToConstructionSite_Cost_IsCheap()
        {
            var action = new GoapAction_GoToConstructionSite(null);
            Assert.AreEqual(0.5f, action.Cost,
                "GoToConstructionSite is cheap so the planner prefers it over alternative moves.");
        }

        [Test]
        public void GoToConstructionSite_ActionName_IsExpected()
        {
            var action = new GoapAction_GoToConstructionSite(null);
            Assert.AreEqual("GoToConstructionSite", action.ActionName);
        }

        [Test]
        public void GoToConstructionSite_IsValid_FalseWithNullAB()
        {
            var action = new GoapAction_GoToConstructionSite(null);
            Assert.IsFalse(action.IsValid(null));
        }

        // ── GoapAction_DropMaterialAtZone ────────────────────────────────

        [Test]
        public void DropMaterialAtZone_Preconditions_RequireBothCarryAndZone()
        {
            var action = new GoapAction_DropMaterialAtZone(null);

            Assert.IsTrue(action.Preconditions.ContainsKey("hasMaterialsInHand"));
            Assert.IsTrue(action.Preconditions["hasMaterialsInHand"]);
            Assert.IsTrue(action.Preconditions.ContainsKey("insideConstructionSite"));
            Assert.IsTrue(action.Preconditions["insideConstructionSite"]);
        }

        [Test]
        public void DropMaterialAtZone_Effects_SetMaterialDelivered()
        {
            var action = new GoapAction_DropMaterialAtZone(null);
            Assert.IsTrue(action.Effects.ContainsKey("materialDelivered"));
            Assert.IsTrue(action.Effects["materialDelivered"]);
        }

        [Test]
        public void DropMaterialAtZone_ActionName_IsExpected()
        {
            var action = new GoapAction_DropMaterialAtZone(null);
            Assert.AreEqual("DropMaterialAtZone", action.ActionName);
        }

        [Test]
        public void DropMaterialAtZone_IsValid_FalseWithNullAB()
        {
            var action = new GoapAction_DropMaterialAtZone(null);
            Assert.IsFalse(action.IsValid(null));
        }

        // ── GoapAction_FinishBuildingConstruction ────────────────────────

        [Test]
        public void FinishBuildingConstruction_Preconditions_RequireDeliveryAndZone()
        {
            var action = new GoapAction_FinishBuildingConstruction(null);

            Assert.IsTrue(action.Preconditions.ContainsKey("insideConstructionSite"));
            Assert.IsTrue(action.Preconditions["insideConstructionSite"]);
            Assert.IsTrue(action.Preconditions.ContainsKey("materialDelivered"));
            Assert.IsTrue(action.Preconditions["materialDelivered"]);
        }

        [Test]
        public void FinishBuildingConstruction_Effects_SetIsIdling()
        {
            var action = new GoapAction_FinishBuildingConstruction(null);
            Assert.IsTrue(action.Effects.ContainsKey("isIdling"));
            Assert.IsTrue(action.Effects["isIdling"],
                "isIdling=true keeps the cascade ending on a valid Idle terminator — multi-trip is handled by JobBuilder's force-replan on action completion.");
        }

        [Test]
        public void FinishBuildingConstruction_ActionName_IsExpected()
        {
            var action = new GoapAction_FinishBuildingConstruction(null);
            Assert.AreEqual("FinishBuildingConstruction", action.ActionName);
        }

        [Test]
        public void FinishBuildingConstruction_IsValid_FalseWithNullAB()
        {
            var action = new GoapAction_FinishBuildingConstruction(null);
            Assert.IsFalse(action.IsValid(null));
        }

        // ── Initial-state guarantees (Exit / replay safety) ──────────────

        [Test]
        public void AllActions_InitialIsComplete_IsFalse()
        {
            var a1 = new GoapAction_TakeMaterialFromABStorage(null);
            var a2 = new GoapAction_GoToConstructionSite(null);
            var a3 = new GoapAction_DropMaterialAtZone(null);
            var a4 = new GoapAction_FinishBuildingConstruction(null);

            Assert.IsFalse(a1.IsComplete);
            Assert.IsFalse(a2.IsComplete);
            Assert.IsFalse(a3.IsComplete);
            Assert.IsFalse(a4.IsComplete);
        }
    }
}
