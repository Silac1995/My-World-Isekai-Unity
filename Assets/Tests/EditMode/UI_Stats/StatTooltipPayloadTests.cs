using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MWI.Tests.UI_Stats
{
    /// <summary>
    /// NOTE: StatType, StatTooltipPayload, StatTooltipPayload.BreakdownLine all live in the
    /// predefined Assembly-CSharp, which Unity does not allow asmdef-defined test
    /// assemblies to reference directly (silently dropped). Tests drive every type
    /// through reflection. See StatDescriptionRegistryTests.cs for the pattern.
    /// </summary>
    public class StatTooltipPayloadTests
    {
        private static Type _statTypeEnum;
        private static Type _payloadType;
        private static Type _breakdownLineType;
        private static MethodInfo _forAttribute;
        private static MethodInfo _forDerived;
        private static MethodInfo _forVital;
        private static FieldInfo _fldType;
        private static FieldInfo _fldDisplayName;
        private static FieldInfo _fldCurrentValue;
        private static FieldInfo _fldDescription;
        private static FieldInfo _fldFormulaString;
        private static FieldInfo _fldBreakdownLines;
        private static FieldInfo _fldPreviewLine;
        private static FieldInfo _fldBLLabel;
        private static FieldInfo _fldBLDelta;
        private static ConstructorInfo _ctorBreakdown;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Assert.That(asm, Is.Not.Null, "Assembly-CSharp must be loaded.");

            _statTypeEnum = asm.GetType("StatType");
            Assert.That(_statTypeEnum, Is.Not.Null, "StatType not found.");

            _payloadType = asm.GetType("StatTooltipPayload");
            Assert.That(_payloadType, Is.Not.Null, "StatTooltipPayload not found.");

            _breakdownLineType = asm.GetType("StatTooltipPayload+BreakdownLine");
            Assert.That(_breakdownLineType, Is.Not.Null, "StatTooltipPayload+BreakdownLine not found.");

            _forAttribute = _payloadType.GetMethod("ForAttribute", BindingFlags.Public | BindingFlags.Static);
            _forDerived   = _payloadType.GetMethod("ForDerived",   BindingFlags.Public | BindingFlags.Static);
            _forVital     = _payloadType.GetMethod("ForVital",     BindingFlags.Public | BindingFlags.Static);
            Assert.That(_forAttribute, Is.Not.Null, "StatTooltipPayload.ForAttribute not found.");
            Assert.That(_forDerived,   Is.Not.Null, "StatTooltipPayload.ForDerived not found.");
            Assert.That(_forVital,     Is.Not.Null, "StatTooltipPayload.ForVital not found.");

            const BindingFlags f = BindingFlags.Public | BindingFlags.Instance;
            _fldType           = _payloadType.GetField("Type",           f);
            _fldDisplayName    = _payloadType.GetField("DisplayName",    f);
            _fldCurrentValue   = _payloadType.GetField("CurrentValue",   f);
            _fldDescription    = _payloadType.GetField("Description",    f);
            _fldFormulaString  = _payloadType.GetField("FormulaString",  f);
            _fldBreakdownLines = _payloadType.GetField("BreakdownLines", f);
            _fldPreviewLine    = _payloadType.GetField("PreviewLine",    f);
            Assert.That(_fldType,           Is.Not.Null, "Type field missing");
            Assert.That(_fldDisplayName,    Is.Not.Null, "DisplayName field missing");
            Assert.That(_fldCurrentValue,   Is.Not.Null, "CurrentValue field missing");
            Assert.That(_fldDescription,    Is.Not.Null, "Description field missing");
            Assert.That(_fldFormulaString,  Is.Not.Null, "FormulaString field missing");
            Assert.That(_fldBreakdownLines, Is.Not.Null, "BreakdownLines field missing");
            Assert.That(_fldPreviewLine,    Is.Not.Null, "PreviewLine field missing");

            _fldBLLabel = _breakdownLineType.GetField("Label", f);
            _fldBLDelta = _breakdownLineType.GetField("Delta", f);
            _ctorBreakdown = _breakdownLineType.GetConstructor(new[] { typeof(string), typeof(float) });
            Assert.That(_ctorBreakdown, Is.Not.Null, "BreakdownLine(string,float) ctor not found.");
        }

        private static object Stat(string name) => Enum.Parse(_statTypeEnum, name);

        private static object NewBreakdown(string label, float delta)
            => _ctorBreakdown.Invoke(new object[] { label, delta });

        // Build IReadOnlyList<BreakdownLine> from an array.
        private static object MakeBreakdownList(params object[] entries)
        {
            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(_breakdownLineType);
            var list = (IList)Activator.CreateInstance(listType);
            foreach (var e in entries) list.Add(e);
            return list;
        }

        [Test]
        public void ForVital_Has_Name_Description_NoFormula_NoPreview()
        {
            var p = _forVital.Invoke(null, new object[] { Stat("Health"), 87f, null });
            Assert.AreEqual("Health", _fldDisplayName.GetValue(p));
            Assert.IsTrue(((string)_fldDescription.GetValue(p)).Length > 0);
            Assert.IsNull(_fldFormulaString.GetValue(p));
            Assert.IsNull(_fldPreviewLine.GetValue(p));
        }

        [Test]
        public void ForAttribute_Carries_Breakdown_And_Optional_Preview()
        {
            var breakdown = MakeBreakdownList(
                NewBreakdown("Base value", 10f),
                NewBreakdown("Steel Pauldrons", 2f));

            var p = _forAttribute.Invoke(null, new object[]
            {
                Stat("Strength"), 12f, breakdown,
                "+1 STR -> Physical Power 14.4 -> 15.6"
            });

            Assert.AreEqual("Strength", _fldDisplayName.GetValue(p));
            var lines = (IList)_fldBreakdownLines.GetValue(p);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("Steel Pauldrons", (string)_fldBLLabel.GetValue(lines[1]));
            Assert.AreEqual(2f, (float)_fldBLDelta.GetValue(lines[1]));
            Assert.IsNull(_fldFormulaString.GetValue(p));
            Assert.AreEqual("+1 STR -> Physical Power 14.4 -> 15.6", _fldPreviewLine.GetValue(p));
        }

        [Test]
        public void ForDerived_Carries_Caller_Supplied_Formula()
        {
            var breakdown = MakeBreakdownList(
                NewBreakdown("From STR (12)", 12f),
                NewBreakdown("From DEX (10)", 2f));

            var p = _forDerived.Invoke(null, new object[]
            {
                Stat("PhysicalPower"), 14f, breakdown,
                "max(0, 0 + STR * 1.0)"
            });

            Assert.AreEqual("Physical Power", _fldDisplayName.GetValue(p));
            Assert.AreEqual("max(0, 0 + STR * 1.0)", _fldFormulaString.GetValue(p));
            var lines = (IList)_fldBreakdownLines.GetValue(p);
            Assert.AreEqual(2, lines.Count);
            Assert.IsNull(_fldPreviewLine.GetValue(p));
        }

        [Test]
        public void Breakdown_May_Be_Null_For_All_Builders()
        {
            var a = _forAttribute.Invoke(null, new object[] { Stat("Agility"),  10f, null, null });
            var v = _forVital    .Invoke(null, new object[] { Stat("Stamina"),  80f, null });
            var d = _forDerived  .Invoke(null, new object[] { Stat("Speed"),    23f, null, null });
            Assert.IsNull(_fldBreakdownLines.GetValue(a));
            Assert.IsNull(_fldBreakdownLines.GetValue(v));
            Assert.IsNull(_fldBreakdownLines.GetValue(d));
        }
    }
}
