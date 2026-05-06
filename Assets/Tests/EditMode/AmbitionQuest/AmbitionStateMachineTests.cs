using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers CharacterAmbition initial-state invariants and Progress01 math.
///
/// NOTE: CharacterAmbition, AmbitionInstance, AmbitionSO, and QuestSO all live in
/// Assembly-CSharp, which Unity does not allow asmdef-defined test assemblies to
/// reference directly (silently dropped when using overrideReferences).
/// Tests drive every type via runtime reflection, following the same pattern as
/// QuestOrderingTests.cs in this folder.
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
        private static Type _ambitionInstanceType;

        // Concrete subclass of AmbitionSO created via IL emission
        private static Type _concreteAmbitionSOType;

        // Reflection handles for internal / inaccessible members
        private static MethodInfo _testForceState;
        private static FieldInfo _ambitionSOQuestsField;

        // CharacterAmbition member access
        private static PropertyInfo _caHasActiveProp;
        private static PropertyInfo _caCurrentProp;
        private static PropertyInfo _caHistoryProp;
        private static PropertyInfo _caCurrentProgress01Prop;

        // AmbitionInstance member access
        private static FieldInfo _instSOField;
        private static FieldInfo _instCurrentStepIndexField;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // ── Locate Assembly-CSharp ────────────────────────────────────────────
            _asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Assert.That(_asm, Is.Not.Null, "Assembly-CSharp must be loaded.");

            // ── Locate types ──────────────────────────────────────────────────────
            _ambitionSOType       = _asm.GetType("MWI.Ambition.AmbitionSO");
            _questSOType          = _asm.GetType("MWI.Ambition.QuestSO");
            _charAmbitionType     = _asm.GetType("MWI.Ambition.CharacterAmbition");
            _ambitionInstanceType = _asm.GetType("MWI.Ambition.AmbitionInstance");

            Assert.That(_ambitionSOType,       Is.Not.Null, "MWI.Ambition.AmbitionSO not found.");
            Assert.That(_questSOType,          Is.Not.Null, "MWI.Ambition.QuestSO not found.");
            Assert.That(_charAmbitionType,     Is.Not.Null, "MWI.Ambition.CharacterAmbition not found.");
            Assert.That(_ambitionInstanceType, Is.Not.Null, "MWI.Ambition.AmbitionInstance not found.");

            // ── Reflect internal TEST_ForceState ──────────────────────────────────
            _testForceState = _charAmbitionType.GetMethod(
                "TEST_ForceState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_testForceState, Is.Not.Null, "CharacterAmbition.TEST_ForceState not found.");

            // ── Reflect private _quests field on AmbitionSO ───────────────────────
            _ambitionSOQuestsField = _ambitionSOType.GetField(
                "_quests", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_ambitionSOQuestsField, Is.Not.Null, "AmbitionSO._quests not found.");

            // ── Reflect public properties on CharacterAmbition ────────────────────
            _caHasActiveProp         = _charAmbitionType.GetProperty("HasActive");
            _caCurrentProp           = _charAmbitionType.GetProperty("Current");
            _caHistoryProp           = _charAmbitionType.GetProperty("History");
            _caCurrentProgress01Prop = _charAmbitionType.GetProperty("CurrentProgress01");

            Assert.That(_caHasActiveProp,         Is.Not.Null, "CharacterAmbition.HasActive not found.");
            Assert.That(_caCurrentProp,           Is.Not.Null, "CharacterAmbition.Current not found.");
            Assert.That(_caHistoryProp,           Is.Not.Null, "CharacterAmbition.History not found.");
            Assert.That(_caCurrentProgress01Prop, Is.Not.Null, "CharacterAmbition.CurrentProgress01 not found.");

            // ── Reflect AmbitionInstance fields ───────────────────────────────────
            _instSOField               = _ambitionInstanceType.GetField("SO");
            _instCurrentStepIndexField = _ambitionInstanceType.GetField("CurrentStepIndex");

            Assert.That(_instSOField,               Is.Not.Null, "AmbitionInstance.SO field not found.");
            Assert.That(_instCurrentStepIndexField, Is.Not.Null, "AmbitionInstance.CurrentStepIndex field not found.");

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
        private Component NewCharacterAmbition()
        {
            var go = new GameObject("TestCharacterAmbition");
            return go.AddComponent(_charAmbitionType);
        }

        /// <summary>
        /// Creates a concrete AmbitionSO and populates its _quests list with
        /// <paramref name="questCount"/> fresh QuestSO instances.
        /// </summary>
        private ScriptableObject NewAmbitionSO(int questCount)
        {
            var ambSO = (ScriptableObject)ScriptableObject.CreateInstance(_concreteAmbitionSOType);

            var listType  = typeof(List<>).MakeGenericType(_questSOType);
            var questList = (IList)Activator.CreateInstance(listType);
            for (int i = 0; i < questCount; i++)
                questList.Add(ScriptableObject.CreateInstance(_questSOType));

            _ambitionSOQuestsField.SetValue(ambSO, questList);
            return ambSO;
        }

        /// <summary>Builds a fresh AmbitionInstance with the given SO + step index via reflection.</summary>
        private object NewAmbitionInstance(ScriptableObject ambSO, int currentStepIndex)
        {
            var inst = Activator.CreateInstance(_ambitionInstanceType);
            _instSOField.SetValue(inst, ambSO);
            _instCurrentStepIndexField.SetValue(inst, currentStepIndex);
            return inst;
        }

        /// <summary>Injects an AmbitionInstance into the CharacterAmbition via the internal test seam.</summary>
        private void ForceState(Component ca, object instance)
        {
            _testForceState.Invoke(ca, new object[] { instance });
        }

        // ── Property accessors via reflection ─────────────────────────────────────
        private static bool   GetHasActive(Component ca)         => (bool)_caHasActiveProp.GetValue(ca);
        private static object GetCurrent(Component ca)           => _caCurrentProp.GetValue(ca);
        private static int    GetHistoryCount(Component ca)
        {
            var history = _caHistoryProp.GetValue(ca);
            return (int)history.GetType().GetProperty("Count").GetValue(history);
        }
        private static float  GetCurrentProgress01(Component ca) => (float)_caCurrentProgress01Prop.GetValue(ca);

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Test]
        public void Initial_State_Is_Inactive()
        {
            var ca = NewCharacterAmbition();

            Assert.IsFalse(GetHasActive(ca),     "Fresh CharacterAmbition must not have an active ambition.");
            Assert.IsNull(GetCurrent(ca),         "Fresh CharacterAmbition.Current must be null.");
            Assert.AreEqual(0, GetHistoryCount(ca), "Fresh CharacterAmbition.History must be empty.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
        }

        [Test]
        public void Progress01_ReturnsZero_WhenInactive()
        {
            var ca = NewCharacterAmbition();

            Assert.AreEqual(0f, GetCurrentProgress01(ca),
                "CurrentProgress01 must be 0 when no ambition is active.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
        }

        [Test]
        public void Progress01_AdvancesAcrossSteps()
        {
            var ca    = NewCharacterAmbition();
            var ambSO = NewAmbitionSO(questCount: 2);

            // Step 0 of 2 → progress = 0/2 = 0f
            var inst0 = NewAmbitionInstance(ambSO, currentStepIndex: 0);
            ForceState(ca, inst0);

            Assert.AreEqual(0f, GetCurrentProgress01(ca), 0.0001f,
                "Progress01 at step 0/2 should be 0.");

            // Step 1 of 2 → progress = 1/2 = 0.5f
            var inst1 = NewAmbitionInstance(ambSO, currentStepIndex: 1);
            ForceState(ca, inst1);

            Assert.AreEqual(0.5f, GetCurrentProgress01(ca), 0.0001f,
                "Progress01 at step 1/2 should be 0.5.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
            UnityEngine.Object.DestroyImmediate(ambSO);
        }

        [Test]
        public void History_StartsEmpty_OnFreshAmbition()
        {
            var ca = NewCharacterAmbition();

            Assert.AreEqual(0, GetHistoryCount(ca),
                "History must start empty on a fresh CharacterAmbition.");

            UnityEngine.Object.DestroyImmediate(ca.gameObject);
        }
    }
}
