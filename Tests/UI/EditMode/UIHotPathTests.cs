using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UIHotPathTests
    {
        private sealed class ThrowingTickTransition : ITickTransitionSource
        {
            public bool Begin(bool open) => throw new InvalidOperationException("tick-begin-failure");
            public bool Advance(float deltaTime) => true;
            public void Snap(bool open) { }
            public UniTask Play(bool open, CancellationToken token) =>
                throw new InvalidOperationException("tick source must not use Play.");
        }

        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [Test]
        public void ThrowingTickTransitionIsLoggedAndOpenCompletes()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            child.Holder.SetTransition(new ThrowingTickTransition());
            LogAssert.Expect(LogType.Exception, new Regex("tick-begin-failure"));
            child.Open();
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
        }

        [Test]
        public void TransitionStartedDuringUpdateStartsAdvancingNextTick()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            window.Updating = () =>
            {
                window.Updating = null;
                child.Open();
            };

            _fixture.Tick();

            Assert.That(source.Begins, Is.EqualTo(1));
            Assert.That(source.Advances, Is.Zero);
            _fixture.Tick();
            Assert.That(source.Advances, Is.EqualTo(1));
        }

        [Test]
        public void OpeningTickTransitionIsDrivenByServiceTick()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            child.Open();
            var transition = child.AwaitTransition();
            Assert.That(source.Begins, Is.EqualTo(1));
            Assert.That(transition.Status.IsCompleted(), Is.False);
            _fixture.Tick();
            Assert.That(source.Advances, Is.EqualTo(1));
            source.Finished = true;
            _fixture.Tick();
            Assert.That(transition.Status.IsCompleted(), Is.True);
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
            _fixture.Tick();
            Assert.That(source.Advances, Is.EqualTo(2));
        }

        [Test]
        public void ClosingTickTransitionIsDrivenByServiceTick()
        {
            UIProbeWidget child = _fixture.Open().Create();
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            child.Close();
            var transition = child.AwaitTransition();
            Assert.That(source.Begins, Is.EqualTo(1));
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            source.Finished = true;
            _fixture.Tick();
            Assert.That(transition.Status.IsCompleted(), Is.True);
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
            Assert.That(child.IsVisible, Is.False);
        }

        [Test]
        public void InstantTickTransitionCompletesInSameCall()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var source = new UITickGateTransition { BeginResult = false };
            child.Holder.SetTransition(source);
            child.Open();
            Assert.That(source.Advances, Is.Zero);
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
            _fixture.Tick();
            Assert.That(source.Advances, Is.Zero);
        }

        [Test]
        public void InterruptingTickTransitionUnregistersWithoutAdvancingFurther()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            child.Open();
            Assert.That(source.Begins, Is.EqualTo(1));
            child.DestroyNow();
            int advances = source.Advances;
            _fixture.Tick();
            Assert.That(source.Advances, Is.EqualTo(advances));
            Assert.That(child.State, Is.EqualTo(UIState.Destroyed));
        }

        [Test]
        public void HolderEventSnapshotRefreshesOnSubscriptionChange()
        {
            var window = _fixture.Open();
            int first = 0, second = 0;
            Action a = () => first++;
            Action b = () => second++;
            window.Holder.OnWindowAfterShowEvent += a;
            window.Holder.InvokeWindowAfterShow();
            window.Holder.OnWindowAfterShowEvent -= a;
            window.Holder.InvokeWindowAfterShow();
            window.Holder.OnWindowAfterShowEvent += b;
            window.Holder.InvokeWindowAfterShow();
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TickWithActiveTransitionDoesNotAllocate()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            child.Open();
            yield return AllocationCapture.Measure("first-active-tick", 1,
                () => _fixture.Tick(), sample => Assert.That(sample.Bytes, Is.LessThanOrEqualTo(128)));
            int advances = source.Advances;
            yield return AllocationCapture.Measure("tick-active-transition", 1000,
                () => { for (int i = 0; i < 1000; i++) _fixture.Tick(); },
                sample => Assert.That(sample.Bytes, Is.Zero));
            Assert.That(source.Advances, Is.EqualTo(advances + 1000));
            source.Finished = true;
            _fixture.Tick();
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
        }

        [UnityTest]
        public IEnumerator HolderEventInvocationDoesNotAllocate()
        {
            var window = _fixture.Open();
            Action callback = () => { };
            window.Holder.OnWindowAfterShowEvent += callback;
            window.Holder.InvokeWindowAfterShow();
            yield return AllocationCapture.Measure("holder-event-invoke", 1000,
                () => { for (int i = 0; i < 1000; i++) window.Holder.InvokeWindowAfterShow(); },
                sample => Assert.That(sample.Bytes, Is.Zero));
        }

        [Test]
        public void RegisterRejectsInvalidLayer()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                UIMetaRegistry.Register(typeof(UIProbeWindow), typeof(UIWindowHolder), UILayer.All));
        }

        [Test]
        public void RegisterRejectsNonUIBaseType()
        {
            Assert.Throws<ArgumentException>(() =>
                UIMetaRegistry.Register(typeof(string), typeof(UIWindowHolder)));
        }

        [Test]
        public void RegisterRejectsNonHolderType()
        {
            Assert.Throws<ArgumentException>(() =>
                UIMetaRegistry.Register(typeof(UIProbeWindow), typeof(UnityEngine.MonoBehaviour)));
        }
    }
}
