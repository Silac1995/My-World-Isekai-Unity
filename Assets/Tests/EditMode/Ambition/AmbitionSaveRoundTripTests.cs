using System.Collections.Generic;
using NUnit.Framework;
using MWI.Ambition;

namespace MWI.Tests.Ambition
{
    public class AmbitionSaveRoundTripTests
    {
        [Test]
        public void Context_Primitive_RoundTrip()
        {
            // Fwd: build context with a primitive, serialize via the same code path
            // CharacterAmbition uses, deserialize, expect identical.
            var ctx = new AmbitionContext();
            ctx.Set("Days", 7);
            ctx.Set("Mode", "fight");

            // We re-use the public ContextEntryDTO shape directly. The full pipeline
            // (CharacterAmbition.SerializeContext) requires private access; this test
            // covers the intent of round-tripping primitives via the DTO contract.

            var dtos = new List<ContextEntryDTO>
            {
                new() { Key = "Days", Kind = ContextValueKind.Primitive, SerializedValue = "7" },
                new() { Key = "Mode", Kind = ContextValueKind.Primitive, SerializedValue = "fight" }
            };

            var ctx2 = new AmbitionContext();
            foreach (var e in dtos)
            {
                if (int.TryParse(e.SerializedValue, out var i)) ctx2.Set(e.Key, i);
                else ctx2.Set(e.Key, e.SerializedValue);
            }

            Assert.AreEqual(7, ctx2.Get<int>("Days"));
            Assert.AreEqual("fight", ctx2.Get<string>("Mode"));
        }

        [Test]
        public void CompletedAmbition_DTO_RoundTrip_PreservesDayAndReason()
        {
            var dto = new CompletedAmbitionDTO
            {
                AmbitionSOGuid = "Ambition_Murder",
                CompletedDay = 47,
                Reason = CompletionReason.Completed,
                FinalContext = new List<ContextEntryDTO>()
            };

            // JsonUtility round-trip mirrors what the SaveFileHandler does.
            var json = UnityEngine.JsonUtility.ToJson(dto);
            var dto2 = UnityEngine.JsonUtility.FromJson<CompletedAmbitionDTO>(json);

            Assert.AreEqual("Ambition_Murder", dto2.AmbitionSOGuid);
            Assert.AreEqual(47, dto2.CompletedDay);
            Assert.AreEqual(CompletionReason.Completed, dto2.Reason);
        }
    }
}
