using System;
using NUnit.Framework;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class AmbitionContextTests
    {
        [Test]
        public void Set_Then_Get_RoundTrip_Primitive()
        {
            var ctx = new AmbitionContext();
            ctx.Set("count", 7);
            Assert.AreEqual(7, ctx.Get<int>("count"));
        }

        [Test]
        public void Get_Missing_Throws()
        {
            var ctx = new AmbitionContext();
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                () => ctx.Get<int>("nope"));
        }

        [Test]
        public void TryGet_Missing_Returns_False()
        {
            var ctx = new AmbitionContext();
            bool found = ctx.TryGet<int>("nope", out var v);
            Assert.IsFalse(found);
            Assert.AreEqual(default(int), v);
        }

        [Test]
        public void Set_NonSerializableType_Throws()
        {
            var ctx = new AmbitionContext();
            Assert.Throws<InvalidOperationException>(
                () => ctx.Set("k", new System.Text.StringBuilder("nope")));
        }

        [Test]
        public void Set_Object_With_Character_Runtime_Type_Allowed()
        {
            // Regression: the type check looks at the runtime value type, not the generic T.
            // Caller may pass <object> when iterating a polymorphic collection.
            var ctx = new AmbitionContext();
            // We don't have a real Character here in EditMode — verify that types
            // declared via AmbitionContext.IsSerializableValueKind succeed.
            // Test with a primitive boxed as object.
            object boxedInt = 42;
            Assert.DoesNotThrow(() => ctx.Set<object>("k", boxedInt));
            Assert.AreEqual(42, ctx.Get<int>("k"));
        }
    }
}
