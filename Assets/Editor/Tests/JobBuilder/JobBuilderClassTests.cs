using NUnit.Framework;
using MWI.WorldSystem;

namespace MWI.Tests.JobBuilder
{
    /// <summary>
    /// Contract tests for the JobBuilder class. EditMode-only — covers the constructor,
    /// JobCategory/JobType identity, schedule, ExecuteIntervalSeconds, and the workplace
    /// type-gate on CanExecute. Full GOAP-cycle verification is PlayMode-MP (Plan 4c).
    /// </summary>
    public class JobBuilderClassTests
    {
        [Test]
        public void Constructor_Defaults_AreSane()
        {
            var job = new global::JobBuilder();
            Assert.AreEqual("Builder", job.JobTitle);
            Assert.AreEqual(JobType.Builder, job.Type);
        }

        [Test]
        public void Constructor_Override_RespectsExplicitTitleAndType()
        {
            var job = new global::JobBuilder("Master Builder", JobType.Builder);
            Assert.AreEqual("Master Builder", job.JobTitle);
            Assert.AreEqual(JobType.Builder, job.Type);
        }

        [Test]
        public void Category_IsBuilder()
        {
            var job = new global::JobBuilder();
            Assert.AreEqual(JobCategory.Builder, job.Category);
        }

        [Test]
        public void ExecuteIntervalSeconds_MatchesHeavyPlanningCadence()
        {
            var job = new global::JobBuilder();
            Assert.AreEqual(0.3f, job.ExecuteIntervalSeconds,
                "JobBuilder is a heavy-planning job, matching JobFarmer's 0.3s GOAP-tick cadence.");
        }

        [Test]
        public void GetWorkSchedule_IsSixToEighteen()
        {
            var job = new global::JobBuilder();
            var schedule = job.GetWorkSchedule();
            Assert.AreEqual(1, schedule.Count, "Default schedule is a single work block.");
            Assert.AreEqual(6, schedule[0].startHour);
            Assert.AreEqual(18, schedule[0].endHour);
            Assert.AreEqual(ScheduleActivity.Work, schedule[0].activity);
        }

        [Test]
        public void CanExecute_FalseWhenNoWorkplace()
        {
            var job = new global::JobBuilder();
            // No worker, no workplace assigned → CanExecute false because IsAssigned false.
            Assert.IsFalse(job.CanExecute());
        }

        [Test]
        public void CurrentBuildOrder_NullWhenNoWorkplace()
        {
            var job = new global::JobBuilder();
            Assert.IsNull(job.CurrentBuildOrder,
                "Without a workplace, there's no LogisticsManager to query — returns null safely.");
        }

        [Test]
        public void HasWorkToDo_FalseWhenNoWorkplace()
        {
            var job = new global::JobBuilder();
            Assert.IsFalse(job.HasWorkToDo());
        }

        [Test]
        public void JobCategory_Builder_AppendedLast()
        {
            // Locks the enum order to "Builder is appended last", catching accidental reorders
            // that would corrupt saved JobCategory values.
            var values = System.Enum.GetValues(typeof(JobCategory));
            Assert.AreEqual((int)JobCategory.Builder, values.Length - 1,
                "JobCategory.Builder must be the last enum value (append-only convention).");
        }

        [Test]
        public void JobType_Builder_IsThirteen()
        {
            Assert.AreEqual(13, (int)JobType.Builder,
                "JobType.Builder is locked to 13 (appended in commit fe36debc — never reorder).");
        }
    }
}
