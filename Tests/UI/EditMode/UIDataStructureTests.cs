using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Tests
{
    public sealed class UITickListTests
    {
        private sealed class TickWidget : UIWidget<UIWidgetHolder> { }

        private static UITickNode Node() => new UITickNode(new TickWidget());

        private static List<UITickNode> Traverse(UITickList list, uint tick, Action<UITickNode> visit = null)
        {
            var visited = new List<UITickNode>();
            list.BeginTraversal();
            while (list.MoveNext(tick) is { } node)
            {
                visited.Add(node);
                visit?.Invoke(node);
            }
            return visited;
        }

        [Test]
        public void TraversalKeepsLinkOrder()
        {
            var list = new UITickList();
            UITickNode a = Node(), b = Node(), c = Node();
            list.Link(a, 0); list.Link(b, 0); list.Link(c, 0);
            Assert.That(Traverse(list, 1), Is.EqualTo(new[] { a, b, c }));
            Assert.That(list.Count, Is.EqualTo(3));
        }

        [Test]
        public void RemovingCurrentNodeContinuesWithNext()
        {
            var list = new UITickList();
            UITickNode a = Node(), b = Node(), c = Node();
            list.Link(a, 0); list.Link(b, 0); list.Link(c, 0);
            List<UITickNode> visited = Traverse(list, 1, node => { if (node == a) list.Unlink(a); });
            Assert.That(visited, Is.EqualTo(new[] { a, b, c }));
            Assert.That(list.First, Is.SameAs(b));
        }

        [Test]
        public void RemovingNextNodeSkipsIt()
        {
            var list = new UITickList();
            UITickNode a = Node(), b = Node(), c = Node();
            list.Link(a, 0); list.Link(b, 0); list.Link(c, 0);
            List<UITickNode> visited = Traverse(list, 1, node => { if (node == a) list.Unlink(b); });
            Assert.That(visited, Is.EqualTo(new[] { a, c }));
        }

        [Test]
        public void RemovingAllNodesEndsTraversal()
        {
            var list = new UITickList();
            UITickNode a = Node(), b = Node(), c = Node();
            list.Link(a, 0); list.Link(b, 0); list.Link(c, 0);
            List<UITickNode> visited = Traverse(list, 1, node =>
            {
                if (node != a) return;
                list.Unlink(a); list.Unlink(b); list.Unlink(c);
            });
            Assert.That(visited, Is.EqualTo(new[] { a }));
            Assert.That(list.Count, Is.Zero);
            Assert.That(list.First, Is.Null);
        }

        [Test]
        public void NodeLinkedDuringTraversalStartsNextTick()
        {
            var list = new UITickList();
            UITickNode a = Node(), b = Node();
            list.Link(a, 0);
            Assert.That(Traverse(list, 1, node => { if (node == a) list.Link(b, 1); }), Is.EqualTo(new[] { a }));
            Assert.That(Traverse(list, 2), Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void RelinkDuringTraversalWaitsForNextTick()
        {
            var list = new UITickList();
            UITickNode a = Node(), b = Node();
            list.Link(a, 0); list.Link(b, 0);
            List<UITickNode> visited = Traverse(list, 1, node =>
            {
                if (node != a) return;
                list.Unlink(b);
                list.Link(b, 1);
            });
            Assert.That(visited, Is.EqualTo(new[] { a }));
            Assert.That(Traverse(list, 2), Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void LastNodeUnlinkResetsTail()
        {
            var list = new UITickList();
            UITickNode a = Node(), b = Node();
            list.Link(a, 0); list.Link(b, 0);
            list.Unlink(b);
            list.Unlink(a);
            list.Link(b, 0);
            Assert.That(Traverse(list, 1), Is.EqualTo(new[] { b }));
        }
    }

    public sealed class UITransitionGateTests
    {
        private static int s_completed;
        private static readonly Action<object> Count = _ => s_completed++;

        [SetUp] public void SetUp() => s_completed = 0;

        [Test]
        public void WaiterArrayIsCreatedOnFirstWaitAndReused()
        {
            var gate = new UITransitionGate();
            gate.Restart();
            gate.Release(gate.Settle());
            Assert.That(gate.WaiterCapacity, Is.Zero);
            gate.Restart();
            gate.OnCompleted(Count, null, GateToken(gate));
            Assert.That(gate.WaiterCapacity, Is.EqualTo(2));
            gate.Release(gate.Settle());
            Assert.That(s_completed, Is.EqualTo(1));
            Assert.That(gate.WaiterCapacity, Is.EqualTo(2));
            gate.Restart();
            for (int i = 0; i < 3; i++) gate.OnCompleted(Count, null, GateToken(gate));
            Assert.That(gate.WaiterCapacity, Is.EqualTo(4));
            gate.Release(gate.Settle());
            Assert.That(s_completed, Is.EqualTo(4));
            Assert.That(gate.WaiterCapacity, Is.EqualTo(4));
        }

        [Test]
        public void TaskStatusFollowsRoundAndExpiredTokenIsCompleted()
        {
            var gate = new UITransitionGate();
            gate.Restart();
            UniTask first = gate.Task;
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Pending));
            gate.Restart();
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            UniTask second = gate.Task;
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Pending));
            gate.Release(gate.Settle());
            Assert.That(second.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(gate.Task.Status, Is.EqualTo(UniTaskStatus.Succeeded));
        }

        [Test]
        public void AllWaitersRunIncludingBeyondInitialCapacity()
        {
            var gate = new UITransitionGate();
            gate.Restart();
            for (int i = 0; i < 5; i++) gate.OnCompleted(Count, null, GateToken(gate));
            gate.Release(gate.Settle());
            Assert.That(s_completed, Is.EqualTo(5));
        }

        [Test]
        public void CompletedTokenRunsContinuationImmediately()
        {
            var gate = new UITransitionGate();
            gate.Restart();
            short token = GateToken(gate);
            gate.Release(gate.Settle());
            gate.OnCompleted(Count, null, token);
            Assert.That(s_completed, Is.EqualTo(1));
        }

        [Test]
        public void RestartHoldsOldWaitersUntilRelease()
        {
            var gate = new UITransitionGate();
            gate.Restart();
            gate.OnCompleted(Count, null, GateToken(gate));
            UITransitionWaiters waiters = gate.Restart();
            Assert.That(s_completed, Is.Zero);
            gate.Release(waiters);
            Assert.That(s_completed, Is.EqualTo(1));
            Assert.That(gate.Task.Status, Is.EqualTo(UniTaskStatus.Pending));
        }

        [Test]
        public void WaiterMayStartNewRoundWithoutJoiningOldSettlement()
        {
            var gate = new UITransitionGate();
            gate.Restart();
            UniTask next = default;
            gate.OnCompleted(_ =>
            {
                gate.Restart();
                next = gate.Task;
                gate.OnCompleted(Count, null, GateToken(gate));
            }, null, GateToken(gate));
            gate.Release(gate.Settle());
            Assert.That(next.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(s_completed, Is.Zero);
            gate.Release(gate.Settle());
            Assert.That(s_completed, Is.EqualTo(1));
        }

        [Test]
        public void ThrowingContinuationDoesNotStopOtherWaiters()
        {
            var gate = new UITransitionGate();
            gate.Restart();
            gate.OnCompleted(_ => throw new InvalidOperationException("expected gate failure"), null, GateToken(gate));
            gate.OnCompleted(Count, null, GateToken(gate));
            LogAssert.Expect(LogType.Exception, new Regex("expected gate failure"));
            gate.Release(gate.Settle());
            Assert.That(s_completed, Is.EqualTo(1));
        }

        [Test]
        public void TokenWrapKeepsCurrentRoundPending()
        {
            var gate = new UITransitionGate();
            for (int i = 0; i < 70000; i++) gate.Release(gate.Settle());
            gate.Restart();
            Assert.That(gate.Task.Status, Is.EqualTo(UniTaskStatus.Pending));
        }

        private static short GateToken(UITransitionGate gate)
        {
            short token = short.MinValue;
            for (int i = short.MinValue; i <= short.MaxValue; i++)
            {
                token = (short)i;
                if (gate.GetStatus(token) == UniTaskStatus.Pending) return token;
            }
            throw new InvalidOperationException("Gate is not pending.");
        }
    }

    public sealed class UIEventProxyTests
    {
        private struct PayloadEvent : IPayloadEventArgs { }
        private struct EmptyEvent : IEmptyEventArgs { }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearEmpty<EmptyEvent>();
            EventBus.ClearPayload<PayloadEvent>();
        }

        [Test]
        public void CapacityGrowsOnDemandAndClearKeepsIt()
        {
            var proxy = new EventListenerProxy();
            Assert.That(proxy.SubscriptionCapacity, Is.Zero);
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                proxy.AddUIEvent<EmptyEvent>(() => GC.KeepAlive(index));
            }
            Assert.That(proxy.SubscriptionCount, Is.EqualTo(5));
            Assert.That(proxy.SubscriptionCapacity, Is.EqualTo(8));
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.EqualTo(5));
            proxy.Suspend();
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.Zero);
            proxy.Resume();
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.EqualTo(5));
            proxy.Clear();
            Assert.That(proxy.SubscriptionCount, Is.Zero);
            Assert.That(proxy.SubscriptionCapacity, Is.EqualTo(8));
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.Zero);
        }

        [Test]
        public void ClearReleasesHandlerReferences()
        {
            var proxy = new EventListenerProxy();
            WeakReference reference = AddTemporaryHandler(proxy);
            proxy.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(reference.IsAlive, Is.False);
            GC.KeepAlive(proxy);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AddTemporaryHandler(EventListenerProxy proxy)
        {
            var target = new object();
            Action handler = () => GC.KeepAlive(target);
            proxy.AddUIEvent<EmptyEvent>(handler);
            return new WeakReference(handler);
        }

        [Test]
        public void SuspendAndResumeKeepSubscriberCountStable()
        {
            var proxy = new EventListenerProxy();
            int events = 0;
            proxy.AddUIEvent<EmptyEvent>(() => events++);
            for (int i = 0; i < 100; i++)
            {
                proxy.Suspend();
                Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.Zero);
                EventBus.Publish<EmptyEvent>();
                proxy.Resume();
                Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.EqualTo(1));
            }
            Assert.That(events, Is.Zero);
            EventBus.Publish<EmptyEvent>();
            Assert.That(events, Is.EqualTo(1));
            proxy.Clear();
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.Zero);
        }

        [Test]
        public void PayloadEventsResume()
        {
            var proxy = new EventListenerProxy();
            int events = 0;
            proxy.AddUIEvent<PayloadEvent>((in PayloadEvent _) => events++);
            proxy.Suspend();
            proxy.Resume();
            Assert.That(EventBus.GetPayloadSubscriberCount<PayloadEvent>(), Is.EqualTo(1));
            proxy.Clear();
            Assert.That(EventBus.GetPayloadSubscriberCount<PayloadEvent>(), Is.Zero);
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void AddWhileSuspendedSubscribesOnResume()
        {
            var proxy = new EventListenerProxy();
            proxy.Suspend();
            proxy.AddUIEvent<EmptyEvent>(() => { });
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.Zero);
            proxy.Resume();
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.EqualTo(1));
            proxy.Clear();
        }

        [Test]
        public void ClearResetsPooledInstance()
        {
            var proxy = new EventListenerProxy();
            proxy.AddUIEvent<EmptyEvent>(() => { });
            proxy.Suspend();
            proxy.Clear();
            Assert.That(proxy.SubscriptionCount, Is.Zero);
            Assert.That(proxy.IsSuspended, Is.False);
            proxy.AddUIEvent<EmptyEvent>(() => { });
            Assert.That(EventBus.GetEmptySubscriberCount<EmptyEvent>(), Is.EqualTo(1));
            proxy.Clear();
        }
    }

    public sealed class UIHolderEventTests
    {
        private GameObject _object;
        private UIWindowHolder _holder;

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("holder-events", typeof(RectTransform));
            _holder = _object.AddComponent<UIWindowHolder>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_object);

        [Test]
        public void AddAndRemoveInCallbackApplyOnNextInvoke()
        {
            int a = 0, b = 0;
            Action second = () => b++;
            Action first = null;
            first = () =>
            {
                a++;
                _holder.OnWindowInitEvent -= first;
                _holder.OnWindowInitEvent += second;
            };
            _holder.OnWindowInitEvent += first;
            _holder.InvokeWindowInit();
            Assert.That(a, Is.EqualTo(1));
            Assert.That(b, Is.Zero);
            _holder.InvokeWindowInit();
            Assert.That(a, Is.EqualTo(1));
            Assert.That(b, Is.EqualTo(1));
        }

        [Test]
        public void SameDelegateTwiceRemovesOneOccurrence()
        {
            int count = 0;
            Action callback = () => count++;
            _holder.OnWindowInitEvent += callback;
            _holder.OnWindowInitEvent += callback;
            _holder.InvokeWindowInit();
            Assert.That(count, Is.EqualTo(2));
            _holder.OnWindowInitEvent -= callback;
            _holder.InvokeWindowInit();
            Assert.That(count, Is.EqualTo(3));
        }

        [Test]
        public void MulticastDelegateIsolatesEachTarget()
        {
            int count = 0;
            Action multicast = () => throw new InvalidOperationException("expected holder failure");
            multicast += () => count++;
            _holder.OnWindowInitEvent += multicast;
            LogAssert.Expect(LogType.Exception, new Regex("expected holder failure"));
            _holder.InvokeWindowInit();
            Assert.That(count, Is.EqualTo(1));
            _holder.OnWindowInitEvent -= multicast;
            _holder.InvokeWindowInit();
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void RemovingAbsentAndNullIsIgnored()
        {
            _holder.OnWindowInitEvent -= () => { };
            _holder.OnWindowInitEvent += null;
            _holder.OnWindowInitEvent -= null;
            _holder.InvokeWindowInit();
        }
    }

    public sealed class UIPresetChannelTests
    {
        private static readonly UITransitionPreset[] Presets = (UITransitionPreset[])Enum.GetValues(typeof(UITransitionPreset));

        private static void Set(UIPresetTransition transition, string field, object value) =>
            typeof(UIPresetTransition).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(transition, value);

        [Test]
        public void MissingTargetsAreSkippedForAllPresets()
        {
            foreach (UITransitionPreset preset in Presets)
            {
                var obj = new GameObject("preset");
                try
                {
                    obj.transform.localScale = new Vector3(2f, 2f, 2f);
                    var transition = obj.AddComponent<UIPresetTransition>();
                    Set(transition, "useUnscaledTime", false);
                    Set(transition, "openPreset", preset);
                    Set(transition, "closePreset", preset);
                    transition.Snap(true);
                    if (transition.Begin(false)) while (!transition.Advance(0.05f)) { }
                    if (transition.Begin(true)) while (!transition.Advance(0.05f)) { }
                    Assert.That(obj.transform.localScale, Is.EqualTo(new Vector3(2f, 2f, 2f)), preset.ToString());
                }
                finally { Object.DestroyImmediate(obj); }
            }
        }

        [Test]
        public void InterruptedTransitionsAlwaysReachBaseline()
        {
            foreach (UITransitionPreset close in Presets)
            foreach (UITransitionPreset open in Presets)
            {
                var obj = new GameObject("preset", typeof(RectTransform), typeof(CanvasGroup));
                try
                {
                    var rect = (RectTransform)obj.transform;
                    var group = obj.GetComponent<CanvasGroup>();
                    rect.anchoredPosition = new Vector2(3f, 5f);
                    rect.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                    group.alpha = 0.8f;
                    var transition = obj.AddComponent<UIPresetTransition>();
                    Set(transition, "targetRect", rect);
                    Set(transition, "canvasGroup", group);
                    Set(transition, "useUnscaledTime", false);
                    Set(transition, "openPreset", open);
                    Set(transition, "closePreset", close);
                    string label = close + " -> " + open;

                    transition.Snap(true);
                    if (transition.Begin(false)) transition.Advance(0.05f);
                    if (transition.Begin(true)) while (!transition.Advance(0.05f)) { }
                    Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(3f, 5f)), "position " + label);
                    Assert.That(rect.localScale, Is.EqualTo(new Vector3(1.5f, 1.5f, 1.5f)), "scale " + label);
                    Assert.That(group.alpha, Is.EqualTo(0.8f).Within(0.0001f), "alpha " + label);

                    if (transition.Begin(false)) while (!transition.Advance(0.05f)) { }
                    if (transition.Begin(true)) transition.Advance(0.05f);
                    if (transition.Begin(false)) while (!transition.Advance(0.05f)) { }
                    transition.Snap(true);
                    Assert.That(rect.localScale, Is.EqualTo(new Vector3(1.5f, 1.5f, 1.5f)), "snap scale " + label);
                }
                finally { Object.DestroyImmediate(obj); }
            }
        }

        [Test]
        public void FadeDoesNotWriteUnchangedChannels()
        {
            var obj = new GameObject("preset", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                var rect = (RectTransform)obj.transform;
                var transition = obj.AddComponent<UIPresetTransition>();
                Set(transition, "targetRect", rect);
                Set(transition, "canvasGroup", obj.GetComponent<CanvasGroup>());
                Set(transition, "useUnscaledTime", false);
                Set(transition, "openPreset", UITransitionPreset.Fade);
                Set(transition, "closePreset", UITransitionPreset.Fade);
                transition.Snap(true);
                Assert.That(transition.Begin(false), Is.True);
                rect.anchoredPosition = new Vector2(100f, 0f);
                rect.localScale = new Vector3(2f, 2f, 2f);
                transition.Advance(0.05f);
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(100f, 0f)));
                Assert.That(rect.localScale, Is.EqualTo(new Vector3(2f, 2f, 2f)));
            }
            finally { Object.DestroyImmediate(obj); }
        }
    }
}
