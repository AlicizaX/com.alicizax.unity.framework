#if UNITY_EDITOR
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using AlicizaX.Audio.Runtime;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Audio.Tests
{
    public sealed class AudioAsyncAllocationTests : AudioTestBase
    {
        private static object probe;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AudioAsyncAllocationProbe() => probe = new byte[4096];

        [UnityTest]
        public IEnumerator CrossFrameRecorderFindsAllocationsAfterSuspension()
        {
            yield return AllocationCapture.MeasureFrames("cross-frame-calibration", Probe(),
                sample => Assert.That(sample.Bytes, Is.GreaterThanOrEqualTo(8192)));
            probe = null;
        }

        private static IEnumerator Probe()
        {
            AudioAsyncAllocationProbe();
            yield return null;
            yield return null;
            AudioAsyncAllocationProbe();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecorderRestoresProfilerWhenMeasuredCodeThrows()
        {
            bool enabled = ProfilerDriver.enabled;
            bool callstacks = Profiler.enableAllocationCallstacks;
            var measurement = AllocationCapture.Measure("expected-exception", 1,
                () => throw new InvalidOperationException("measurement failure"), _ => Assert.Fail("Must not verify failed workload."));
            Assert.That(measurement.MoveNext(), Is.True);
            yield return measurement.Current;
            Assert.Throws<InvalidOperationException>(() => measurement.MoveNext());
            Assert.That(ProfilerDriver.enabled, Is.EqualTo(enabled));
            Assert.That(Profiler.enableAllocationCallstacks, Is.EqualTo(callstacks));
        }

        [UnityTest]
        public IEnumerator SharedLoadsIncludeCompletionDispatchReleaseAndRepeatedCancellation()
        {
            using var f = new AudioFixture(voices: 64);
            f.Clip("a");
            f.Loader.CompleteImmediately = false;
            for (int pass = 0; pass < 4; pass++)
            {
                int callbacks = 0, successes = 0;
                Action<bool> completed = success => { if (success) successes++; callbacks++; };
                yield return AllocationCapture.MeasureFrames("complete-64-voices-pass-" + pass, Complete(f, completed),
                    sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.LessThanOrEqualTo(pass == 0 ? 128 * 1024 : 64)), pass == 3);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(successes, Is.EqualTo(1));
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                f.CheckOwnership();
                yield return AllocationCapture.MeasureFrames("cancel-64-voices-pass-" + pass, Cancel(f),
                    sample => Assert.That(sample.Bytes - sample.BackendBytes, Is.LessThanOrEqualTo(pass == 0 ? 16 * 1024 : 0)), pass == 3);
                Assert.That(f.Loader.LiveHandles, Is.Zero);
                f.CheckOwnership();
            }
            Assert.That(f.Loader.Loads, Is.EqualTo(8));
        }

        private static IEnumerator Complete(AudioFixture f, Action<bool> completed)
        {
            for (int i = 0; i < 64; i++) Assert.That(f.Audio.PlayAsync(AudioType.Sound, "a", true), Is.Not.Zero);
            f.Audio.PreloadAsync("a", AudioCachePolicy.Ttl, completed);
            f.CheckActive(AudioType.Sound, 64, 64);
            yield return null;
            yield return null;
            f.Loader.Complete("a");
            yield return AudioFixture.Frames();
            f.CheckActive(AudioType.Sound, 64, 64);
            Assert.That(f.Entry("a").RefCount, Is.EqualTo(64));
            f.Audio.StopAll(false);
            Assert.That(f.Audio.Unload("a"), Is.True);
            yield return AudioFixture.Frames();
        }

        private static IEnumerator Cancel(AudioFixture f)
        {
            for (int i = 0; i < 64; i++) Assert.That(f.Audio.PlayAsync(AudioType.Sound, "a", true), Is.Not.Zero);
            f.CheckActive(AudioType.Sound, 64, 64);
            yield return null;
            f.Audio.StopAll(false);
            yield return AudioFixture.Frames();
        }
    }
}
#endif
