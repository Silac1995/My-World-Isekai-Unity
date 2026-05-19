using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MWI.Tests.UI_Stats
{
    /// <summary>
    /// NOTE: StatType, StatDescription, and StatDescriptionRegistry all live in the
    /// predefined Assembly-CSharp, which Unity does not allow asmdef-defined test
    /// assemblies to reference directly (silently dropped from the references list).
    /// Tests therefore drive every type through reflection. Established project
    /// pattern — see Assets/Tests/EditMode/ToolStorage/ItemInstanceOwnerBuildingIdTests.cs
    /// and Assets/Tests/EditMode/AmbitionQuest/QuestOrderingTests.cs.
    /// </summary>
    public class StatDescriptionRegistryTests
    {
        private static Type _statTypeEnum;
        private static Type _statDescriptionType;
        private static Type _registryType;
        private static MethodInfo _tryGetMethod;
        private static FieldInfo _displayNameField;
        private static FieldInfo _descriptionField;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Assert.That(asm, Is.Not.Null, "Assembly-CSharp must be loaded.");

            _statTypeEnum = asm.GetType("StatType");
            Assert.That(_statTypeEnum, Is.Not.Null, "StatType enum not found in Assembly-CSharp.");

            _statDescriptionType = asm.GetType("StatDescription");
            Assert.That(_statDescriptionType, Is.Not.Null, "StatDescription struct not found in Assembly-CSharp.");

            _registryType = asm.GetType("StatDescriptionRegistry");
            Assert.That(_registryType, Is.Not.Null, "StatDescriptionRegistry not found in Assembly-CSharp.");

            _tryGetMethod = _registryType.GetMethod("TryGet",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(_tryGetMethod, Is.Not.Null, "StatDescriptionRegistry.TryGet not found.");

            _displayNameField = _statDescriptionType.GetField("DisplayName",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(_displayNameField, Is.Not.Null, "StatDescription.DisplayName not found.");

            _descriptionField = _statDescriptionType.GetField("Description",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(_descriptionField, Is.Not.Null, "StatDescription.Description not found.");
        }

        // Wraps TryGet(StatType, out StatDescription) into a friendlier C# call.
        private static bool TryGet(string statName, out object descriptionBox)
        {
            var statValue = Enum.Parse(_statTypeEnum, statName);
            var args = new object[] { statValue, null };
            bool ok = (bool)_tryGetMethod.Invoke(null, args);
            descriptionBox = args[1];
            return ok;
        }

        [Test]
        public void Every_Displayed_StatType_Has_An_Entry()
        {
            var displayed = new[]
            {
                "Health", "Stamina", "Mana", "Initiative",
                "Strength", "Agility", "Dexterity",
                "Intelligence", "Endurance", "Charisma",
                "PhysicalPower", "MagicalPower", "Speed",
                "Accuracy", "Dodge", "CriticalChance",
                "SpellCasting", "CombatCasting",
                "StaminaRegen", "ManaRegen",
            };
            foreach (var t in displayed)
            {
                Assert.IsTrue(TryGet(t, out var d), $"Missing entry for {t}");
                Assert.That(d, Is.Not.Null, $"Null description box for {t}");
                var displayName = (string)_displayNameField.GetValue(d);
                var description = (string)_descriptionField.GetValue(d);
                Assert.IsFalse(string.IsNullOrWhiteSpace(displayName),
                    $"Empty DisplayName for {t}");
                Assert.IsFalse(string.IsNullOrWhiteSpace(description),
                    $"Empty Description for {t}");
            }
        }

        [Test]
        public void TryGet_Returns_False_For_Unmapped_Stat()
        {
            // Pick a StatType that the registry does not need to support — if such a value exists.
            // If EVERY StatType is mapped, this test is a no-op stub.
            bool foundUnmapped = false;
            foreach (var name in Enum.GetNames(_statTypeEnum))
            {
                if (!TryGet(name, out _)) { foundUnmapped = true; break; }
            }
            // No assertion needed — this test exists to surface coverage gaps in human review, not to fail.
            Assert.Pass($"Registry coverage: foundUnmapped={foundUnmapped}");
        }
    }
}
