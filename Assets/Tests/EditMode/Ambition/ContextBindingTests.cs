using NUnit.Framework;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class ContextBindingTests
    {
        [Test]
        public void Resolve_ReadsValueFromContextKey()
        {
            var ctx = new AmbitionContext();
            ctx.Set("Days", 7);
            var binding = new ContextBinding<int> { Key = "Days" };
            Assert.AreEqual(7, binding.Resolve(ctx));
        }

        [Test]
        public void CanResolve_FalseWhenKeyMissing()
        {
            var ctx = new AmbitionContext();
            var binding = new ContextBinding<int> { Key = "Days" };
            Assert.IsFalse(binding.CanResolve(ctx));
        }

        [Test]
        public void CanResolve_TrueWhenKeyPresent()
        {
            var ctx = new AmbitionContext();
            ctx.Set("Days", 7);
            var binding = new ContextBinding<int> { Key = "Days" };
            Assert.IsTrue(binding.CanResolve(ctx));
        }

        [Test]
        public void CanResolve_FalseWhenContextIsNull()
        {
            var binding = new ContextBinding<int> { Key = "Days" };
            Assert.IsFalse(binding.CanResolve(null));
        }

        [Test]
        public void CanResolve_FalseWhenKeyIsEmpty()
        {
            var ctx = new AmbitionContext();
            ctx.Set("Days", 7);
            var binding = new ContextBinding<int> { Key = "" };
            Assert.IsFalse(binding.CanResolve(ctx));
        }
    }
}
