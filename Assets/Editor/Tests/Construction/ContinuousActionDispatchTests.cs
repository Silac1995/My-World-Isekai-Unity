using NUnit.Framework;

public class ContinuousActionDispatchTests
{
    private class FakeContinuousAction : CharacterAction_Continuous
    {
        public int TickCount;
        public int TerminateAfterTicks = 3;

        public FakeContinuousAction() : base(character: null) { }

        public override void OnStart() { }

        public override bool OnTick()
        {
            TickCount++;
            return TickCount >= TerminateAfterTicks;
        }
    }

    [Test]
    public void OnTick_ReportsContinue_UntilTerminationCondition()
    {
        var action = new FakeContinuousAction { TerminateAfterTicks = 3 };

        Assert.IsFalse(action.OnTick(), "First tick should keep ticking");
        Assert.IsFalse(action.OnTick(), "Second tick should keep ticking");
        Assert.IsTrue(action.OnTick(), "Third tick should terminate");
    }

    [Test]
    public void OnApplyEffect_IsSealedNoOp()
    {
        var action = new FakeContinuousAction();
        Assert.DoesNotThrow(() => action.OnApplyEffect());
    }

    [Test]
    public void Duration_Defaults_To_Zero()
    {
        var action = new FakeContinuousAction();
        Assert.AreEqual(0f, action.Duration);
    }
}
