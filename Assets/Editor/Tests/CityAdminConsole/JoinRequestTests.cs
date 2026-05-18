using NUnit.Framework;

namespace MWI.Tests.CityAdminConsole
{
    /// <summary>
    /// Contract tests for <see cref="JoinRequest"/> — INetworkSerializable struct backing
    /// the AB's PendingJoinRequests NetworkList.
    ///
    /// Plan 4c Task 6.
    /// </summary>
    public class JoinRequestTests
    {
        [Test]
        public void DefaultJoinRequest_HasZeroFields()
        {
            var jr = new JoinRequest();
            Assert.AreEqual(0UL, jr.ApplicantNetId);
            Assert.AreEqual(0, jr.RequestedAtDay);
        }

        [Test]
        public void Equality_KeyedOnApplicantNetId()
        {
            var a = new JoinRequest { ApplicantNetId = 42, RequestedAtDay = 1 };
            var b = new JoinRequest { ApplicantNetId = 42, RequestedAtDay = 99 };
            var c = new JoinRequest { ApplicantNetId = 43, RequestedAtDay = 1 };

            Assert.IsTrue(a.Equals(b),
                "Same ApplicantNetId must be equal regardless of RequestedAtDay (dedup-on-resubmit semantic).");
            Assert.IsFalse(a.Equals(c),
                "Different ApplicantNetId must be unequal.");
        }

        [Test]
        public void Equality_BoxedObjectComparison_WorksToo()
        {
            var a = new JoinRequest { ApplicantNetId = 42, RequestedAtDay = 1 };
            object boxed = new JoinRequest { ApplicantNetId = 42, RequestedAtDay = 99 };
            Assert.IsTrue(a.Equals(boxed));
        }

        [Test]
        public void GetHashCode_StableForSameApplicantNetId()
        {
            var a = new JoinRequest { ApplicantNetId = 42, RequestedAtDay = 1 };
            var b = new JoinRequest { ApplicantNetId = 42, RequestedAtDay = 99 };
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(),
                "Hash code keyed on ApplicantNetId; RequestedAtDay must not affect it.");
        }
    }
}
