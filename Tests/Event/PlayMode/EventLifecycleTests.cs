using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.EventTests
{
    public sealed class EventLifecycleTests
    {
        [UnityTest]
        public IEnumerator DestroyedOwnerRemainsSubscribedUntilHandleDisposed()
        {
            foreach (bool payload in new[] { false, true })
            {
                using var f = new EventFixture(payload);
                var owner = new GameObject("event-owner").AddComponent<EventOwner>();
                owner.Payload = payload; owner.ReadDestroyedObject = true; owner.Bind();
                var handle = owner.Handle;
                var healthy = new Listener(); f.Subscribe(healthy);
                Object.Destroy(owner.gameObject); yield return null;
                for (int frame = 0; frame < 2; frame++)
                {
                    LogAssert.Expect(LogType.Exception, new Regex("MissingReferenceException"));
                    f.Publish(11);
                    Assert.That(healthy.Calls, Is.EqualTo(frame + 1));
                    f.VerifyLedger(2);
#if UNITY_EDITOR
                    EventDebugRegistry.TryGetDetails(payload ? typeof(PayloadProbe) : typeof(EmptyProbe), out _, out var entries);
                    Assert.That(Array.Exists(entries, entry => entry.IsUnityObjectDestroyed), Is.True);
#endif
                    yield return null;
                }
                handle.Dispose(); f.Publish(); Assert.That(healthy.Calls, Is.EqualTo(3)); f.VerifyLedger(1);
            }
        }

        [UnityTest]
        public IEnumerator DisableDestroyAndPoolReuseFollowExplicitOwnership()
        {
            foreach (bool payload in new[] { false, true })
            {
                using var f = new EventFixture(payload);
                var owner = new GameObject("event-reused-owner").AddComponent<EventOwner>();
                owner.Payload = payload; owner.Bind();
                owner.gameObject.SetActive(false); f.Publish();
                Assert.That(owner.Calls, Is.EqualTo(1)); f.VerifyLedger(1);
                owner.Handle.Dispose();
                owner.DisposeOnDisable = true; owner.DisposeOnDestroy = true; owner.SubscribeOnEnable = true;
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    owner.gameObject.SetActive(true); f.Publish(17); f.Publish(23);
                    Assert.That(owner.Calls, Is.EqualTo(3 + cycle * 2)); f.VerifyLedger(1);
                    owner.gameObject.SetActive(false); f.Publish(); f.VerifyLedger(0);
                    yield return null;
                }
                owner.gameObject.SetActive(true); Object.Destroy(owner.gameObject); yield return null;
                f.Publish(); f.VerifyLedger(0);
            }
        }

        [UnityTest]
        public IEnumerator NullUiDoesNotInterruptSameFrameOrNextFramePublish()
        {
            foreach (bool payload in new[] { false, true })
            {
                using var f = new EventFixture(payload);
                var first = new Listener(); f.Subscribe(first);
                var owner = new GameObject("event-null-ui").AddComponent<EventOwner>();
                owner.Payload = payload; owner.ReadUi = true; owner.DisposeOnDestroy = true; owner.Bind();
                var last = new Listener(); f.Subscribe(last);
                for (int frame = 0; frame < 2; frame++)
                {
                    foreach (int value in new[] { 3, 5, 7 })
                    {
                        LogAssert.Expect(LogType.Exception, new Regex("NullReferenceException"));
                        f.Publish(value);
                    }
                    Assert.That(first.Calls, Is.EqualTo((frame + 1) * 3));
                    Assert.That(last.Calls, Is.EqualTo(first.Calls)); f.VerifyLedger(3);
                    if (payload) Assert.That(last.Sum, Is.EqualTo((frame + 1) * 15));
                    yield return null;
                }
                Object.Destroy(owner.gameObject); yield return null; f.VerifyLedger(2);
            }
        }

        [UnityTest]
        public IEnumerator ReusedUiEventProxyReleasesEveryHandle()
        {
            using var f = new EventFixture(false);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var proxy = MemoryPool<EventListenerProxy>.Acquire();
                var listener = new Listener(); proxy.AddUIEvent<EmptyProbe>(listener.OnEmpty);
                f.Publish(); Assert.That(listener.Calls, Is.EqualTo(1));
                MemoryPool.Release(proxy); f.VerifyLedger(0);
                f.Publish(); Assert.That(listener.Calls, Is.EqualTo(1)); yield return null;
            }
        }
    }
}
