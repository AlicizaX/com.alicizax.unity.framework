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
        [Test]
        public void ReadonlyLeaseAccessDoesNotCopyTheLease()
        {
            var method = typeof(ResourceValueTypeTests).GetMethod(nameof(ReadLease), BindingFlags.Static | BindingFlags.NonPublic);
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
            TestContext.WriteLine($"STRUCT,ResourceAssetLease,{SizeOf(typeof(ResourceAssetLease<TextAsset>))},readonly-read-copies={copies}");
            Assert.That(copies, Is.Zero, "Property getters on an in Lease must not emit a defensive struct copy.");
        }

        [Test]
        public void RecordActualManagedStructSizes()
        {
            foreach (var type in new[] { typeof(ResourceKey), typeof(ResourceAssetLease<TextAsset>), typeof(ResourceLeaseHandle), typeof(ResourceLoadLifetime),
                         typeof(ResourceAssetInfo), typeof(ResourceBindingInfo), typeof(ResourceOwnerInfo) })
                TestContext.WriteLine($"STRUCT,{type.Name},{SizeOf(type)}");
            foreach (var container in new[] { typeof(ResourceService), typeof(ResourceBindingService) })
            foreach (var type in container.GetNestedTypes(BindingFlags.NonPublic))
                if (type.IsValueType) TestContext.WriteLine($"STRUCT,{type.Name},{SizeOf(type)}");
        }

        private static int ReadLease(in ResourceAssetLease<TextAsset> lease)
            => (lease.Asset == null ? 0 : 1) + lease.Handle.Index + (lease.IsValid ? 1 : 0);

        private static int SizeOf(Type type)
        {
            var method = new DynamicMethod("SizeOf", typeof(int), Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Sizeof, type);
            il.Emit(OpCodes.Ret);
            return ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
        }
    }
}
