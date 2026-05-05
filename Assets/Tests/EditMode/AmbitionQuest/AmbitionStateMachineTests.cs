using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MWI.Ambition;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers CharacterAmbition initial-state invariants and Progress01 math.
///
/// NOTE: AmbitionSO is abstract and TEST_ForceState is internal to Assembly-CSharp.
/// We therefore:
///   - Emit a concrete ConcreteAmbitionSO subclass at runtime via IL emission so we
///     can call ScriptableObject.CreateInstance on it.
///   - Access TEST_ForceState via reflection (internal, not visible cross-assembly).
///   - AmbitionInstance and QuestSO are used directly (Assembly-CSharp reference in asmdef).
/// </summary>
namespace MWI.Tests.AmbitionQuest
{
    public class AmbitionStateMachineTests
    {
        // ── Type handles ──────────────────────────────────────────────────────────
        private static Assembly _asm;
        private static Type _ambitionSOType;
        private static Type _questSOType;
        private static Type _charAmbitionType;

        // Concrete subclass of AmbitionSO created via IL emission
        private static Type _concreteAmbitionSOType;

        // Reflection handles for internal / inaccessible members
        private static MethodInfo _testForceState;
        private static FieldInfo _ambitionSOQuestsField;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // ── Locate Assembly-CSharp ────────────────────────────────────────────
            _asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Assert.That(_asm, Is.Not.Null, "Assembly-CSharp must be loaded.");

            // ── Locate types ──────────────────────────────────────────────────────
            _ambitionSOType    = _asm.GetType("MWI.Ambition.AmbitionSO");
            _questSOType       = _asm.GetType("MWI.Ambition.QuestSO");
            _charAmbitionType  = _asm.GetType("MWI.Ambition.CharacterAmbition");

            Assert.That(_ambitionSOType,   Is.Not.Null, "MWI.Ambition.AmbitionSO not found.");
            Assert.That(_questSOType,      Is.Not.Null, "MWI.Ambition.QuestSO not found.");
            Assert.That(_charAmbitionType, Is.Not.Null, "MWI.Ambition.CharacterAmbition not found.");

            // ── Reflect internal TEST_ForceState ──────────────────────────────────
            _testForceState = _charAmbitionType.GetMethod(
                "TEST_ForceState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_testForceState, Is.Not.Null, "CharacterAmbition.TEST_ForceState not found.");

            // ── Reflect private _quests field on AmbitionSO ───────────────────────
            _ambitionSOQuestsField = _ambitionSOType.GetField(
                "_quests", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_ambitionSOQuestsField, Is.Not.Null, "AmbitionSO._quests not found.");

            // ── Emit concrete subclass of AmbitionSO ─────────────────────────────
            _concreteAmbitionSOType = BuildConcreteAmbitionSOType();
            Assert.That(_concreteAmbitionSOType, Is.Not.Null, "Failed to emit ConcreteAmbitionSO.");
        }

        // ── Dynamic type builder ──────────────────────────────────────────────────

        /// <summary>
        /// Emits a minimal concrete subclass of the abstract AmbitionSO so that
        /// ScriptableObject.CreateInstance can instantiate it.
        /// </summary>
        private static Type BuildConcreteAmbitionSOType()
        {
            var asmName    = new AssemblyName("AmbitionStateMachineTests.Dynamic");
            var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            var modBuilder = asmBuilder.DefineDynamicModule("Main");

            var tb = modBuilder.DefineType(
                "ConcreteAmbitionSO",
                TypeAttributes.Public | TypeAttributes.Class,
                _ambitionSOType);

            // Default constructor — just calls base()
            var ctor = tb.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            {
                var il = ctor.GetILGenerator();
                var baseCtor = _ambitionSOType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                il.Emit(OpCodes.Ldarg_0);
                if (baseCtor != null) il.Emit(OpCodes.Call, baseCtor);
                il.Emit(OpCodes.Ret);
            }

            return tb.CreateType();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Creates a fresh CharacterAmbition via AddComponent (no network spawn needed).</summary>
        private CharacterAmbition NewCharacterAmbition()
        {
            var go = new GameObject("TestCharacterAmbition");
            return (CharacterAmbition)go.AddComponent(_charAmbitionType);
        }

        /// <summary>
        /// Creates a concrete AmbitionSO and populates its _quests list with
        /// <paramref name="questCount"/> fresh QuestSO instances.
        /// </summary>
        private ScriptableObject NewAmbitionSO(int questCount)
        {
            var ambSO = (ScriptableObject)ScriptableObject.CreateInstance(_concreteAmbitionSOType);

            var listType  = typeof(List<>).MakeGenericType(_questSOType);
            var questList = (System.Collections.IList)Activator.CreateInstance(listType);
            for (int i = 0; i < questCount; i++)
                questList.Add(ScriptableObject.CreateInstance(_questSOType));

            _ambitionSOQuestsField.SetValue(ambSO, questList);
            return ambSO;
        }

        /// <summary>Injects an AmbitionInstance into the CharacterAmbition via the internal test seam.</summary>
        private void ForceState(CharacterAmbition ca, AmbitionInstance instance)
        {
            _testForceState.Invoke(ca, new object[] { instance });
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Test]
        public void Initial_State_Is_Inactive()
        {
            var ca = NewCharacterAmbition();

            Assert.IsFalse(ca.HasActive,       "Fresh CharacterAmbition must not have an active ambition.");
            Assert.IsNull(ca.Current,           "Fresh CharacterAmbition.Current must be null.");
            Assert.AreEqual(0, ca.History.Count, "Fresh CharacterAmbition.History must be empty.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
        }

        [Test]
        public void Progress01_ReturnsZero_WhenInactive()
        {
            var ca = NewCharacterAmbition();

            Assert.AreEqual(0f, ca.CurrentProgress01,
                "CurrentProgress01 must be 0 when no ambition is active.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
        }

        [Test]
        public void Progress01_AdvancesAcrossSteps()
        {
            var ca    = NewCharacterAmbition();
            var ambSO = NewAmbitionSO(questCount: 2);

            // Step 0 of 2 → progress = 0/2 = 0f
            var inst0 = new AmbitionInstance
            {
                SO               = (AmbitionSO)ambSO,
                CurrentStepIndex = 0,
            };
            ForceState(ca, inst0);

            Assert.AreEqual(0f, ca.CurrentProgress01, 0.0001f,
                "Progress01 at step 0/2 should be 0.");

            // Step 1 of 2 → progress = 1/2 = 0.5f
            var inst1 = new AmbitionInstance
            {
                SO               = (AmbitionSO)ambSO,
                CurrentStepIndex = 1,
            };
            ForceState(ca, inst1);

            Assert.AreEqual(0.5f, ca.CurrentProgress01, 0.0001f,
                "Progress01 at step 1/2 should be 0.5.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
            UnityEngine.Object.DestroyImmediate(ambSO);
        }

        [Test]
        public void History_StartsEmpty_OnFreshAmbition()
        {
            var ca = NewCharacterAmbition();

            Assert.AreEqual(0, ca.History.Count,
                "History must start empty on a fresh CharacterAmbition.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
        }
    }
}
