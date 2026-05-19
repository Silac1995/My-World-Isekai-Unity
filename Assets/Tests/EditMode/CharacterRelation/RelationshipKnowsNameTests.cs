using NUnit.Framework;

namespace MWI.Tests.CharacterRelation
{
    public class RelationshipKnowsNameTests
    {
        [Test]
        public void NewRelationship_KnowsName_DefaultsFalse()
        {
            var rel = new Relationship(null, null);
            Assert.IsFalse(rel.KnowsName, "Fresh Relationship must default KnowsName to false.");
        }

        [Test]
        public void SetKnowsName_True_FlipsFlag()
        {
            var rel = new Relationship(null, null);
            rel.SetKnowsName(true);
            Assert.IsTrue(rel.KnowsName);
        }

        [Test]
        public void SetKnowsName_False_ClearsFlag()
        {
            var rel = new Relationship(null, null);
            rel.SetKnowsName(true);
            rel.SetKnowsName(false);
            Assert.IsFalse(rel.KnowsName);
        }

        [Test]
        public void KnowsName_IndependentFromHasMet()
        {
            var rel = new Relationship(null, null);
            rel.SetAsMet();
            Assert.IsFalse(rel.KnowsName, "HasMet=true must not auto-flip KnowsName.");
            rel.SetKnowsName(true);
            rel.SetAsNotMet();
            Assert.IsTrue(rel.KnowsName, "SetAsNotMet must not auto-flip KnowsName.");
        }
    }
}
