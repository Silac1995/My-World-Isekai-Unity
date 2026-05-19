using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.UI_Stats
{
    /// <summary>
    /// NOTE: StatType, StatPreviewCalculator and its nested types all live in the
    /// predefined Assembly-CSharp, which Unity does not allow asmdef-defined test
    /// assemblies to reference directly (silently dropped). Tests drive every type
    /// through reflection. See StatDescriptionRegistryTests.cs for the pattern.
    /// </summary>
    public class StatPreviewCalculatorTests
    {
        // ── Type handles ──────────────────────────────────────────────────────
        private static Type _statTypeEnum;
        private static Type _calcType;
        private static Type _snapshotType;
        private static Type _scalingTableType;
        private static Type _scalingEntryType;
        private static Type _previewLineType;

        // ── Method / member handles ───────────────────────────────────────────
        private static ConstructorInfo _snapshotCtor;     // (float,float,float,float,float,float)
        private static ConstructorInfo _scalingTableCtor; // ()
        private static MethodInfo      _scalingSet;       // Set(StatType,StatType,float,float,float)
        private static MethodInfo      _previewPlusOne;   // (Snapshot, StatType, ScalingTable) -> IEnumerable<PreviewLine>

        private static FieldInfo _plDerived;
        private static FieldInfo _plBefore;
        private static FieldInfo _plAfter;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Assert.That(asm, Is.Not.Null, "Assembly-CSharp must be loaded.");

            _statTypeEnum     = asm.GetType("StatType");
            _calcType         = asm.GetType("StatPreviewCalculator");
            _snapshotType     = asm.GetType("StatPreviewCalculator+Snapshot");
            _scalingTableType = asm.GetType("StatPreviewCalculator+ScalingTable");
            _scalingEntryType = asm.GetType("StatPreviewCalculator+ScalingEntry");
            _previewLineType  = asm.GetType("StatPreviewCalculator+PreviewLine");
            Assert.That(_statTypeEnum,     Is.Not.Null, "StatType not found.");
            Assert.That(_calcType,         Is.Not.Null, "StatPreviewCalculator not found.");
            Assert.That(_snapshotType,     Is.Not.Null, "StatPreviewCalculator+Snapshot not found.");
            Assert.That(_scalingTableType, Is.Not.Null, "StatPreviewCalculator+ScalingTable not found.");
            Assert.That(_scalingEntryType, Is.Not.Null, "StatPreviewCalculator+ScalingEntry not found.");
            Assert.That(_previewLineType,  Is.Not.Null, "StatPreviewCalculator+PreviewLine not found.");

            _snapshotCtor = _snapshotType.GetConstructor(new[]
            {
                typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float)
            });
            Assert.That(_snapshotCtor, Is.Not.Null, "Snapshot(float,float,float,float,float,float) ctor not found.");

            _scalingTableCtor = _scalingTableType.GetConstructor(Type.EmptyTypes);
            Assert.That(_scalingTableCtor, Is.Not.Null, "ScalingTable() ctor not found.");

            _scalingSet = _scalingTableType.GetMethod("Set", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(_scalingSet, Is.Not.Null, "ScalingTable.Set not found.");

            _previewPlusOne = _calcType.GetMethod("PreviewPlusOne",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(_previewPlusOne, Is.Not.Null, "StatPreviewCalculator.PreviewPlusOne not found.");

            const BindingFlags f = BindingFlags.Public | BindingFlags.Instance;
            _plDerived = _previewLineType.GetField("DerivedStat", f);
            _plBefore  = _previewLineType.GetField("Before",      f);
            _plAfter   = _previewLineType.GetField("After",       f);
            Assert.That(_plDerived, Is.Not.Null);
            Assert.That(_plBefore,  Is.Not.Null);
            Assert.That(_plAfter,   Is.Not.Null);
        }

        private static object Stat(string name) => Enum.Parse(_statTypeEnum, name);

        private static object MakeSnapshot(float str, float agi, float dex, float intel, float end, float cha)
            => _snapshotCtor.Invoke(new object[] { str, agi, dex, intel, end, cha });

        private static object NewTable() => _scalingTableCtor.Invoke(null);

        private static void Set(object table, string derived, string linked,
            float multiplier, float baseOffset, float minValue)
        {
            _scalingSet.Invoke(table, new object[]
            {
                Stat(derived), Stat(linked), multiplier, baseOffset, minValue
            });
        }

        private static object DefaultScaling()
        {
            // Default wiring per recon §5b: every tertiary x 1.0 + 0, linked to its secondary.
            var t = NewTable();
            Set(t, "PhysicalPower",  "Strength",     1f, 0f, 0f);
            Set(t, "MagicalPower",   "Intelligence", 1f, 0f, 0f);
            Set(t, "Speed",          "Agility",      1f, 0f, 0f);
            Set(t, "Accuracy",       "Dexterity",    1f, 0f, 0f);
            Set(t, "Dodge",          "Agility",      1f, 0f, 0f);
            Set(t, "CriticalChance", "Dexterity",    1f, 0f, 0f);
            Set(t, "SpellCasting",   "Dexterity",    1f, 0f, 0f);
            Set(t, "CombatCasting",  "Agility",      1f, 0f, 0f);
            Set(t, "StaminaRegen",   "Endurance",    1f, 0f, 0f);
            Set(t, "ManaRegen",      "Intelligence", 1f, 0f, 0f);
            return t;
        }

        private static List<object> Preview(object snapshot, string bumped, object table)
        {
            var raw = _previewPlusOne.Invoke(null, new object[] { snapshot, Stat(bumped), table });
            var result = new List<object>();
            foreach (var x in (IEnumerable)raw) result.Add(x);
            return result;
        }

        private static bool MatchesDerived(object previewLine, string derivedName)
            => Equals(_plDerived.GetValue(previewLine), Stat(derivedName));

        [Test]
        public void Plus1_STR_Boosts_PhysicalPower_By_Multiplier()
        {
            var snap = MakeSnapshot(10, 10, 10, 10, 10, 10);
            var scaling = DefaultScaling();
            var results = Preview(snap, "Strength", scaling);
            var pp = results.First(r => MatchesDerived(r, "PhysicalPower"));
            Assert.AreEqual(10f, (float)_plBefore.GetValue(pp), 0.001f);
            Assert.AreEqual(11f, (float)_plAfter .GetValue(pp), 0.001f);
        }

        [Test]
        public void Plus1_AGI_Affects_Speed_Dodge_CombatCasting()
        {
            var snap = MakeSnapshot(10, 10, 10, 10, 10, 10);
            var scaling = DefaultScaling();
            var results = Preview(snap, "Agility", scaling);
            Assert.IsTrue(results.Any(r => MatchesDerived(r, "Speed")         && (float)_plAfter.GetValue(r) > (float)_plBefore.GetValue(r)));
            Assert.IsTrue(results.Any(r => MatchesDerived(r, "Dodge")         && (float)_plAfter.GetValue(r) > (float)_plBefore.GetValue(r)));
            Assert.IsTrue(results.Any(r => MatchesDerived(r, "CombatCasting") && (float)_plAfter.GetValue(r) > (float)_plBefore.GetValue(r)));
        }

        [Test]
        public void Plus1_CHA_Affects_Nothing()
        {
            var snap = MakeSnapshot(10, 10, 10, 10, 10, 10);
            var scaling = DefaultScaling();
            var results = Preview(snap, "Charisma", scaling);
            Assert.IsTrue(results.All(r =>
                Mathf.Approximately((float)_plBefore.GetValue(r), (float)_plAfter.GetValue(r))));
        }

        [Test]
        public void Custom_Scaling_Is_Honored()
        {
            var snap = MakeSnapshot(10, 10, 10, 10, 10, 10);
            var scaling = NewTable();
            Set(scaling, "PhysicalPower", "Strength", 2.5f, 1f, 0f);

            var results = Preview(snap, "Strength", scaling);
            var pp = results.First(r => MatchesDerived(r, "PhysicalPower"));
            // Before: max(0, 1 + 10 * 2.5) = 26
            // After:  max(0, 1 + 11 * 2.5) = 28.5
            Assert.AreEqual(26f,   (float)_plBefore.GetValue(pp), 0.001f);
            Assert.AreEqual(28.5f, (float)_plAfter .GetValue(pp), 0.001f);
        }

        [Test]
        public void MinValue_Clamps_Below_Zero()
        {
            var snap = MakeSnapshot(1, 1, 1, 1, 1, 1);
            var scaling = NewTable();
            Set(scaling, "PhysicalPower", "Strength", 1f, -100f, 5f);

            var results = Preview(snap, "Strength", scaling);
            var pp = results.First(r => MatchesDerived(r, "PhysicalPower"));
            // Before: max(5, -100 + 1 * 1) = 5
            // After:  max(5, -100 + 1 * 2) = 5
            Assert.AreEqual(5f, (float)_plBefore.GetValue(pp), 0.001f);
            Assert.AreEqual(5f, (float)_plAfter .GetValue(pp), 0.001f);
        }
    }
}
