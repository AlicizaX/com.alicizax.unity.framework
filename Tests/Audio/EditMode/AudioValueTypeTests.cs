using System;
using System.Reflection.Emit;
using AlicizaX.Audio.Runtime;
using AlicizaX.Resource.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioValueTypeTests
    {
        [Test]
        public void RecordManagedOptionAndLeaseSizes()
        {
            foreach (var type in new[] { typeof(AudioPlayOptions), typeof(AudioSpatialOptions), typeof(Vector3), typeof(ResourceAssetLease<AudioClip>) })
            {
                var method = new DynamicMethod("AudioStructSize", typeof(int), Type.EmptyTypes);
                var il = method.GetILGenerator();
                il.Emit(OpCodes.Sizeof, type);
                il.Emit(OpCodes.Ret);
                int size = ((Func<int>)method.CreateDelegate(typeof(Func<int>)))();
                TestContext.WriteLine($"STRUCT,{type.Name},{size}");
                Assert.That(size, Is.InRange(1, 64));
            }
        }
    }
}
