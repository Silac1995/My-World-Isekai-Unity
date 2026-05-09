using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Covers the three TaskOrderingMode policies (Sequential / Parallel / AnyOf)
/// on AmbitionQuest.TickActiveTasks.
///
/// NOTE: AmbitionQuest, QuestSO, TaskBase, TaskStatus, TaskOrderingMode, and
/// Character all live in the predefined Assembly-CSharp, which Unity does not
/// allow asmdef-defined test assemblies to reference directly (silently dropped).
/// Tests therefore drive every type via runtime reflection, following the same
/// pattern as Assets/Tests/EditMode/ToolStorage/ItemInstanceOwnerBuildingIdTests.cs.
/// </summary>
namespace MWI.Tests.AmbitionQuest
{
    public class QuestOrderingTests
    {
        // ── Assembly-CSharp type handles ──────────────────────────────────────
        private static Assembly _asm;
        private static Type _taskBaseType;
        private static Type _taskStatusType;
        private static Type _taskOrderingModeType;
        private static Type _questSOType;
        private static Type _ambitionQuestType;
        private static Type _ambitionContextType;

        // Cached reflection members for AmbitionQuest
        private static MethodInfo _bindContextMethod;
        private static MethodInfo _tickActiveTasksMethod;
        private static FieldInfo _aqTasksField;
        private static FieldInfo _aqCompletedCountField;

        // Cached reflection members for QuestSO
        private static FieldInfo _soOrderingField;

        // TaskStatus enum values
        private static object _statusRunning;
        private static object _statusCompleted;

        // TaskOrderingMode enum values
        private static object _modeSequential;
        private static object _modeParallel;
        private static object _modeAnyOf;

        // ── Dynamic CountTask type built once ─────────────────────────────────
        // CountTask is a dynamically-emitted subclass of TaskBase. Instances
        // expose two int fields (CompleteAfterTicks, Ticks) and a bool (Cancelled)
        // that tests read via reflection after calling TickActiveTasks.
        private static Type _countTaskType;
        private static FieldInfo _ctCompleteAfterTicks;
        private static FieldInfo _ctTicks;
        private static FieldInfo _ctCancelled;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // ── Locate Assembly-CSharp ────────────────────────────────────────
            _asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Assert.That(_asm, Is.Not.Null, "Assembly-CSharp must be loaded.");

            // ── Locate types ──────────────────────────────────────────────────
            // AmbitionQuest, QuestSO, TaskBase, TaskStatus, TaskOrderingMode live
            // in Assembly-CSharp.  AmbitionContext lives in MWI.Ambition.Pure.
            _taskBaseType         = _asm.GetType("MWI.Ambition.TaskBase");
            _taskStatusType       = _asm.GetType("MWI.Ambition.TaskStatus");
            _taskOrderingModeType = _asm.GetType("MWI.Ambition.TaskOrderingMode");
            _questSOType          = _asm.GetType("MWI.Ambition.QuestSO");
            _ambitionQuestType    = _asm.GetType("MWI.Ambition.AmbitionQuest");

            // AmbitionContext is in the MWI.Ambition.Pure asmdef — search all assemblies.
            var pureAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "MWI.Ambition.Pure");
            Assert.That(pureAsm, Is.Not.Null, "MWI.Ambition.Pure assembly must be loaded.");
            _ambitionContextType = pureAsm.GetType("MWI.Ambition.AmbitionContext");

            Assert.That(_taskBaseType,         Is.Not.Null, "MWI.Ambition.TaskBase not found in Assembly-CSharp.");
            Assert.That(_taskStatusType,       Is.Not.Null, "MWI.Ambition.TaskStatus not found in Assembly-CSharp.");
            Assert.That(_taskOrderingModeType, Is.Not.Null, "MWI.Ambition.TaskOrderingMode not found in Assembly-CSharp.");
            Assert.That(_questSOType,          Is.Not.Null, "MWI.Ambition.QuestSO not found in Assembly-CSharp.");
            Assert.That(_ambitionQuestType,    Is.Not.Null, "MWI.Ambition.AmbitionQuest not found in Assembly-CSharp.");
            Assert.That(_ambitionContextType,  Is.Not.Null, "MWI.Ambition.AmbitionContext not found in MWI.Ambition.Pure.");

            // ── Enum values ───────────────────────────────────────────────────
            _statusRunning   = Enum.Parse(_taskStatusType, "Running");
            _statusCompleted = Enum.Parse(_taskStatusType, "Completed");
            _modeSequential  = Enum.Parse(_taskOrderingModeType, "Sequential");
            _modeParallel    = Enum.Parse(_taskOrderingModeType, "Parallel");
            _modeAnyOf       = Enum.Parse(_taskOrderingModeType, "AnyOf");

            // ── AmbitionQuest members ─────────────────────────────────────────
            _bindContextMethod = _ambitionQuestType.GetMethod(
                "BindContext", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(_bindContextMethod, Is.Not.Null, "AmbitionQuest.BindContext not found.");

            _tickActiveTasksMethod = _ambitionQuestType.GetMethod(
                "TickActiveTasks", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(_tickActiveTasksMethod, Is.Not.Null, "AmbitionQuest.TickActiveTasks not found.");

            _aqTasksField = _ambitionQuestType.GetField(
                "_tasks", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_aqTasksField, Is.Not.Null, "AmbitionQuest._tasks not found.");

            _aqCompletedCountField = _ambitionQuestType.GetField(
                "_completedCount", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_aqCompletedCountField, Is.Not.Null, "AmbitionQuest._completedCount not found.");

            // ── QuestSO members ───────────────────────────────────────────────
            _soOrderingField = _questSOType.GetField(
                "_ordering", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_soOrderingField, Is.Not.Null, "QuestSO._ordering not found.");

            // ── Build dynamic CountTask ───────────────────────────────────────
            _countTaskType = BuildCountTaskType();
            _ctCompleteAfterTicks = _countTaskType.GetField("CompleteAfterTicks");
            _ctTicks              = _countTaskType.GetField("Ticks");
            _ctCancelled          = _countTaskType.GetField("Cancelled");

            Assert.That(_ctCompleteAfterTicks, Is.Not.Null);
            Assert.That(_ctTicks,              Is.Not.Null);
            Assert.That(_ctCancelled,          Is.Not.Null);
        }

        // ── Dynamic type builder ───────────────────────────────────────────────

        /// <summary>
        /// Emits a concrete subclass of TaskBase with the shape:
        ///   public int  CompleteAfterTicks = 1;
        ///   public int  Ticks             = 0;
        ///   public bool Cancelled         = false;
        ///   public override void Bind(AmbitionContext ctx) { }
        ///   public override TaskStatus Tick(Character npc, AmbitionContext ctx) { Ticks++; return Ticks >= CompleteAfterTicks ? Completed : Running; }
        ///   public override void Cancel() { Cancelled = true; }
        /// </summary>
        private static Type BuildCountTaskType()
        {
            var asmName    = new AssemblyName("AmbitionTests.Dynamic");
            var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            var modBuilder = asmBuilder.DefineDynamicModule("Main");
            var tb = modBuilder.DefineType(
                "CountTask",
                TypeAttributes.Public | TypeAttributes.Class,
                _taskBaseType);

            // Public fields
            var fldCompleteAfter = tb.DefineField("CompleteAfterTicks", typeof(int), FieldAttributes.Public);
            var fldTicks         = tb.DefineField("Ticks",              typeof(int), FieldAttributes.Public);
            var fldCancelled     = tb.DefineField("Cancelled",          typeof(bool), FieldAttributes.Public);

            // Default constructor — sets CompleteAfterTicks = 1
            var ctor = tb.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            {
                var il = ctor.GetILGenerator();
                // call base()
                var baseCtor = _taskBaseType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                il.Emit(OpCodes.Ldarg_0);
                if (baseCtor != null) il.Emit(OpCodes.Call, baseCtor);
                // this.CompleteAfterTicks = 1
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stfld, fldCompleteAfter);
                il.Emit(OpCodes.Ret);
            }

            // Bind(AmbitionContext) — no-op override
            {
                var bindBase = _taskBaseType.GetMethod("Bind",
                    BindingFlags.Instance | BindingFlags.Public);
                var mb = tb.DefineMethod("Bind",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    typeof(void), new[] { _ambitionContextType });
                tb.DefineMethodOverride(mb, bindBase);
                var il = mb.GetILGenerator();
                il.Emit(OpCodes.Ret);
            }

            // Tick(Character, AmbitionContext) : TaskStatus
            // { Ticks++; return Ticks >= CompleteAfterTicks ? Completed : Running; }
            {
                // Character type — may be null if we pass null; we just need the signature
                var characterType = _asm.GetType("Character") ?? typeof(object);
                var tickBase = _taskBaseType.GetMethod("Tick",
                    BindingFlags.Instance | BindingFlags.Public);
                var mb = tb.DefineMethod("Tick",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    _taskStatusType, new[] { characterType, _ambitionContextType });
                tb.DefineMethodOverride(mb, tickBase);
                var il = mb.GetILGenerator();
                var labelRunning = il.DefineLabel();

                // Ticks++
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fldTicks);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stfld, fldTicks);

                // if (Ticks < CompleteAfterTicks) goto labelRunning
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fldTicks);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fldCompleteAfter);
                il.Emit(OpCodes.Blt, labelRunning);

                // return Completed (= 1)
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);

                // labelRunning: return Running (= 0)
                il.MarkLabel(labelRunning);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
            }

            // Cancel() { Cancelled = true; }
            {
                var cancelBase = _taskBaseType.GetMethod("Cancel",
                    BindingFlags.Instance | BindingFlags.Public);
                var mb = tb.DefineMethod("Cancel",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    typeof(void), Type.EmptyTypes);
                tb.DefineMethodOverride(mb, cancelBase);
                var il = mb.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Stfld, fldCancelled);
                il.Emit(OpCodes.Ret);
            }

            return tb.CreateType();
        }

        // ── Test helpers ───────────────────────────────────────────────────────

        private object NewCountTask(int completeAfterTicks)
        {
            var inst = Activator.CreateInstance(_countTaskType);
            _ctCompleteAfterTicks.SetValue(inst, completeAfterTicks);
            return inst;
        }

        private bool IsCancelled(object task) => (bool)_ctCancelled.GetValue(task);

        /// <summary>
        /// Build a QuestSO with the given ordering and construct an AmbitionQuest.
        /// Then replace _tasks with our stubs (bypassing the JSON clone so we hold
        /// direct references) and call BindContext.
        /// </summary>
        private object MakeQuest(object orderingMode, params object[] tasks)
        {
            // Create QuestSO and set ordering
            var so = ScriptableObject.CreateInstance(_questSOType);
            _soOrderingField.SetValue(so, orderingMode);

            // Construct AmbitionQuest(so, null, new AmbitionContext())
            var ctx     = Activator.CreateInstance(_ambitionContextType);
            var ctorAQ  = _ambitionQuestType.GetConstructor(new[]
            {
                _questSOType,
                _asm.GetType("Character") ?? typeof(object),
                _ambitionContextType
            });
            Assert.That(ctorAQ, Is.Not.Null, "AmbitionQuest(QuestSO, Character, AmbitionContext) ctor not found.");
            var quest = ctorAQ.Invoke(new object[] { so, null, ctx });

            // Inject stub tasks directly into _tasks (bypassing JSON clone)
            var taskList = (System.Collections.IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(_taskBaseType));
            foreach (var t in tasks) taskList.Add(t);
            _aqTasksField.SetValue(quest, taskList);

            // Reset _completedCount to 0
            _aqCompletedCountField.SetValue(quest, 0);

            // Call BindContext with a fresh context
            var ctx2 = Activator.CreateInstance(_ambitionContextType);
            _bindContextMethod.Invoke(quest, new[] { ctx2 });

            return quest;
        }

        private object TickQuest(object quest)
        {
            return _tickActiveTasksMethod.Invoke(quest, new object[] { null });
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        [Test]
        public void Sequential_AdvancesTaskByTask()
        {
            // Each task completes in exactly 1 tick. Sequential ticks one task per
            // TickActiveTasks call: call 1 ticks t1 (Completed) -> returns Running
            // because t2 is still pending; call 2 ticks t2 (Completed) -> all done.
            var t1 = NewCountTask(1); // completes on its 1st tick
            var t2 = NewCountTask(1); // completes on its 1st tick (which is TickActiveTasks call 2)
            var quest = MakeQuest(_modeSequential, t1, t2);

            // Call 1: ticks t1 -> Completed (_completedCount=1), returns Running (t2 pending)
            var result1 = TickQuest(quest);
            Assert.AreEqual(_statusRunning, result1,
                "Sequential call 1 should return Running (t2 still pending).");

            // Call 2: ticks t2 -> Completed; all tasks done
            var result2 = TickQuest(quest);
            Assert.AreEqual(_statusCompleted, result2,
                "Sequential call 2 should return Completed (both tasks done).");
        }

        [Test]
        public void Parallel_CompletesWhenAllDone()
        {
            var t1 = NewCountTask(1); // completes on tick 1
            var t2 = NewCountTask(2); // needs 2 ticks
            var quest = MakeQuest(_modeParallel, t1, t2);

            // Call 1: t1 -> Completed, t2 -> Running; not all done
            var result1 = TickQuest(quest);
            Assert.AreEqual(_statusRunning, result1,
                "Parallel call 1 should return Running (t2 still pending).");

            // Call 2: t1 -> Completed (idempotent), t2 -> Completed; all done
            var result2 = TickQuest(quest);
            Assert.AreEqual(_statusCompleted, result2,
                "Parallel call 2 should return Completed (both tasks done).");
        }

        [Test]
        public void AnyOf_CompletesOnFirstAndCancelsOthers()
        {
            var t1 = NewCountTask(1); // completes immediately
            var t2 = NewCountTask(5); // would need 5 ticks
            var quest = MakeQuest(_modeAnyOf, t1, t2);

            // Single tick: t1 completes; quest resolves
            var result = TickQuest(quest);
            Assert.AreEqual(_statusCompleted, result,
                "AnyOf should complete as soon as the first task completes.");

            // t2 must have been cancelled
            Assert.IsTrue(IsCancelled(t2),
                "AnyOf should Cancel siblings of the winning task.");
        }
    }
}
