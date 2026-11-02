using System;
using System.Reflection;
using System.Reflection.Emit;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.Resource.Tests
{
    public sealed class ResourceValueTypeTests
    {
        private const BindingFlags Nested = BindingFlags.NonPublic | BindingFlags.Public;

        [TestCase(typeof(AssetRef<TextAsset>), 4)]
        [TestCase(typeof(ResourceScope), 16)]
        [TestCase(typeof(AssetLease<TextAsset>), 24)]
        public void PublicHandlesKeepTheirMeasuredSize(Type type, int expected)
        {
            int size = SizeOf(type);
            TestContext.WriteLine($"STRUCT,{type.Name},{size}");
            Assert.That(size, Is.EqualTo(expected));
        }

        [TestCase(typeof(ResourceService), "AssetRecord", 48)]
        [TestCase(typeof(ResourceService), "EntrySlot", 16)]
        [TestCase(typeof(ResourceService), "ScopeSlot", 32)]
        [TestCase(typeof(ResourceBindingService), "BindingSlot", 64)]
        public void InternalSlotsStayWithinTheirBudget(Type container, string name, int maximum)
        {
            var type = container.GetNestedType(name, Nested);
            Assert.That(type, Is.Not.Null, name);
            int size = SizeOf(type);
            TestContext.WriteLine($"STRUCT,{name},{size}");
            Assert.That(size, Is.LessThanOrEqualTo(maximum));
        }

        [Test]
        public void KeyRecordStaysWithinItsBudget()
        {
            int size = SizeOf(typeof(ResourceAssetKey));
            TestContext.WriteLine($"STRUCT,ResourceAssetKey,{size}");
            Assert.That(size, Is.LessThanOrEqualTo(24));
        }

        [TestCase(typeof(AssetRef<TextAsset>))]
        [TestCase(typeof(ResourceScope))]
        [TestCase(typeof(AssetLease<TextAsset>))]
        public void PublicHandlesAreReadonly(Type type)
        {
            Assert.That(IsReadonly(type), Is.True, type.Name);
        }

        [TestCase(typeof(ResourceService), "LoadIntoScope")]
        [TestCase(typeof(ResourceService), "LoadIntoScopeAsync")]
        [TestCase(typeof(ResourceBindingService), "Bind")]
        [TestCase(typeof(ResourceBindingExtensions), "SetSprite")]
        [TestCase(typeof(ResourceBindingExtensions), "SetSubSprite")]
        [TestCase(typeof(ResourceBindingExtensions), "SetMaterial")]
        [TestCase(typeof(ResourceBindingExtensions), "SetSharedMaterial")]
        [TestCase(typeof(ResourceBindingExtensions), "SetMaterialInstance")]
        public void ScopeEntryPointsTakeTheScopeByReadonlyReference(Type type, string name)
        {
            int checkedParameters = 0;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != name) continue;
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.ParameterType.GetElementType() != typeof(ResourceScope) && parameter.ParameterType != typeof(ResourceScope)) continue;
                    checkedParameters++;
                    Assert.That(parameter.ParameterType.IsByRef && parameter.IsIn, Is.True, method.ToString());
                }
            }
            Assert.That(checkedParameters, Is.GreaterThan(0), name);
        }

        [Test]
        public void ReadingInParametersEmitsNoDefensiveCopy()
        {
            Assert.That(CountLdobj(nameof(ReadLease)), Is.Zero);
            Assert.That(CountLdobj(nameof(ReadScope)), Is.Zero);
        }

        [Test]
        public void RecordDiagnosticStructSizes()
        {
            foreach (var type in new[] { typeof(ResourceAssetInfo), typeof(ResourceScopeInfo), typeof(ResourceStats), typeof(ResourceBindingInfo) })
                TestContext.WriteLine($"STRUCT,{type.Name},{SizeOf(type)}");
        }

        private static int ReadLease(in AssetLease<TextAsset> lease)
            => (lease.Asset == null ? 0 : 1) + (lease.IsValid ? 1 : 0);

        private static int ReadScope(in ResourceScope scope)
            => scope.IsAlive ? scope.Index : 0;

        private static bool IsReadonly(Type type)
        {
            foreach (var attribute in type.GetCustomAttributes(false))
                if (attribute.GetType().Name == "IsReadOnlyAttribute") return true;
            return false;
        }

        private static int CountLdobj(string methodName)
        {
            var method = typeof(ResourceValueTypeTests).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            byte[] body = method.GetMethodBody().GetILAsByteArray();
            int copies = 0;
            for (int offset = 0; offset < body.Length;)
            {
                short value = body[offset++];
                if (value == 0xfe) value = (short)(0xfe00 | body[offset++]);
                OpCode opcode = default;
                foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var candidate = (OpCode)field.GetValue(null);
                    if (candidate.Value == value) { opcode = candidate; break; }
                }
                if (opcode == OpCodes.Ldobj) copies++;
                switch (opcode.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: offset++; break;
                    case OperandType.InlineVar: offset += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: offset += 8; break;
                    case OperandType.InlineSwitch: offset += 4 + BitConverter.ToInt32(body, offset) * 4; break;
                    default: offset += 4; break;
                }
            }
            TestContext.WriteLine($"IL,{methodName},ldobj={copies}");
            return copies;
        }

        private static int SizeOf(Type type)
        {
            var method = new DynamicMethod("SizeOf", typeof(int), Type.EmptyTypes, typeof(ResourceValueTypeTests).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Sizeof, type);
            il.Emit(OpCodes.Ret);
            return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
        }
    }
}
