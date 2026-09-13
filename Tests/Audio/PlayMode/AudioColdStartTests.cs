#if UNITY_EDITOR
using System;
using System.Collections;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioColdStartTests : AudioTestBase
    {
        [UnityTest]
        public IEnumerator FirstAudioTypesAndDemandGrowthStayWithinDeclaredAllocationBudgets()
        {
            var infos = new MemoryPoolInfo[MemoryPool.Count];
            int count = MemoryPool.GetAllMemoryPoolInfos(infos);
            for (int i = 0; i < count; i++) Assert.That(infos[i].Type.Namespace, Is.Not.EqualTo(typeof(AudioService).Namespace), "This test must run first or alone in a fresh process.");
            AudioService service = null;
            yield return AllocationCapture.Measure("cold-service-construction", 1, () => service = new AudioService(),
                sample => Assert.That(sample.Bytes, Is.InRange(1, 8192)));
            service.Shutdown();
            using var f = new AudioFixture(32, voices: 64, initialize: false);
            yield return AllocationCapture.Measure("cold-service-initialize", 1, () => f.Reinitialize(),
                sample => Assert.That(sample.Bytes, Is.InRange(1, 128 * 1024)), true);
            f.Clip("cold-a");
            f.Clip("cold-b");
            ulong first = 0;
            yield return AllocationCapture.Measure("cold-first-audio-types", 1,
                () => first = f.Audio.Play(AudioType.Sound, "cold-a", true),
                sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.InRange(1, 64 * 1024)), true);
            Assert.That(first, Is.Not.Zero);
            f.CheckActive(AudioType.Sound, 1, 1);
            f.CheckOwnership();
            f.Audio.Stop(first);
            int accepted = 0;
            yield return AllocationCapture.Measure("cold-capacity-growth-to-64", 64, () =>
            {
                for (int i = 0; i < 64; i++) if (f.Audio.Play(AudioType.Sound, "cold-a", true) != 0) accepted++;
            }, sample => Assert.That(sample.Bytes, Is.InRange(1, 64 * 1024)));
            Assert.That(accepted, Is.EqualTo(64));
            f.CheckActive(AudioType.Sound, 64, 64);
            f.Audio.StopAll(false);
            ulong second = 0;
            yield return AllocationCapture.Measure("cold-new-resource-address", 1,
                () => second = f.Audio.Play(AudioType.Sound, "cold-b", true),
                sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.LessThanOrEqualTo(8192)), true);
            Assert.That(second, Is.Not.Zero);
            Assert.That(f.Loader.Loads, Is.EqualTo(2));
            f.Audio.Stop(second);
            int stopped = 0;
            yield return AllocationCapture.Measure("cold-demand-grown-steady-state", 10000, () =>
            {
                for (int i = 0; i < 10000; i++) if (f.Audio.Stop(f.Audio.Play(AudioType.Sound, "cold-a", true))) stopped++;
            }, sample => Assert.That(sample.Bytes, Is.Zero), true);
            Assert.That(stopped, Is.EqualTo(10000));
            f.CheckOwnership();
        }
    }
}
#endif
