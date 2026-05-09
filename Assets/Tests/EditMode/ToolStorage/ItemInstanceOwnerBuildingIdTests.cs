using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEngine;

namespace MWI.Tests.ToolStorage
{
    /// <summary>
    /// NOTE: ItemInstance lives in the predefined Assembly-CSharp, which Unity does not allow
    /// asmdef-defined assemblies to reference (silently dropped from the references list).
    /// Tests therefore drive the type through reflection. They still verify the runtime
    /// contract — defaults, get/set, JSON round-trip via JsonUtility, and clear-to-empty —
    /// against the real ItemInstance type loaded from Assembly-CSharp.
    /// </summary>
    public class ItemInstanceOwnerBuildingIdTests
    {
        private static Type _itemInstanceType;
        private static Type _testItemInstanceType;
        private static PropertyInfo _ownerBuildingIdProp;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var assemblyCSharp = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            Assert.That(assemblyCSharp, Is.Not.Null, "Assembly-CSharp must be loaded.");

            _itemInstanceType = assemblyCSharp.GetType("ItemInstance");
            Assert.That(_itemInstanceType, Is.Not.Null, "ItemInstance type must exist in Assembly-CSharp.");

            _ownerBuildingIdProp = _itemInstanceType.GetProperty(
                "OwnerBuildingId",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(_ownerBuildingIdProp, Is.Not.Null, "ItemInstance must expose public OwnerBuildingId property.");

            // Build a runtime test double because ItemInstance is abstract.
            _testItemInstanceType = BuildTestItemInstanceType(_itemInstanceType);
        }

        private static Type BuildTestItemInstanceType(Type baseType)
        {
            var asmName = new AssemblyName("ToolStorageTests.Dynamic");
            var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            var modBuilder = asmBuilder.DefineDynamicModule("Main");
            var typeBuilder = modBuilder.DefineType(
                "TestItemInstance",
                TypeAttributes.Public | TypeAttributes.Class,
                baseType);

            // Ctor that calls base(ItemSO).
            var itemSOType = baseType.Assembly.GetType("ItemSO");
            var baseCtor = baseType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { itemSOType },
                null);
            Assert.That(baseCtor, Is.Not.Null, "ItemInstance(ItemSO) ctor must exist.");

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                new[] { itemSOType });
            var il = ctorBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, baseCtor);
            il.Emit(OpCodes.Ret);

            return typeBuilder.CreateType();
        }

        private static object NewInstance()
        {
            return Activator.CreateInstance(_testItemInstanceType, new object[] { null });
        }

        private static string GetOwner(object instance) => (string)_ownerBuildingIdProp.GetValue(instance);
        private static void SetOwner(object instance, string value) => _ownerBuildingIdProp.SetValue(instance, value);

        [Test]
        public void OwnerBuildingId_Defaults_ToNullOrEmpty()
        {
            var instance = NewInstance();
            Assert.That(string.IsNullOrEmpty(GetOwner(instance)), Is.True);
        }

        [Test]
        public void OwnerBuildingId_SettableAndReadable()
        {
            var instance = NewInstance();
            SetOwner(instance, "guid-abc-123");
            Assert.That(GetOwner(instance), Is.EqualTo("guid-abc-123"));
        }

        [Test]
        public void OwnerBuildingId_RoundTripsThroughJsonSerialization()
        {
            var instance = NewInstance();
            SetOwner(instance, "guid-zzz-999");

            string json = JsonUtility.ToJson(instance);
            var copy = NewInstance();
            JsonUtility.FromJsonOverwrite(json, copy);

            Assert.That(GetOwner(copy), Is.EqualTo("guid-zzz-999"));
        }

        [Test]
        public void OwnerBuildingId_CanBeClearedToEmptyString()
        {
            var instance = NewInstance();
            SetOwner(instance, "guid-xyz");
            SetOwner(instance, "");
            Assert.That(GetOwner(instance), Is.Empty);
        }
    }
}
