#if UNITY_EDITOR
using System;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    public abstract class UIWidgetStateMatrixCases
    {
        private UIFixture _fixture;
        private UIService Service => _fixture.Service;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private void CloseParent() => Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
        private void DestroyParent() => Service.CloseUI<UIProbeWindow>(force: true).GetAwaiter().GetResult();

        private UIGateTransition Opening(UIProbeWidget child)
        {
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Open("first");
            Assert.That(child.State, Is.EqualTo(UIState.Opening));
            return gate;
        }

        private UIGateTransition Closing(UIProbeWidget child)
        {
            child.Open("first");
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Close();
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            return gate;
        }

        [Test]
        public void CreatedUI_ParentCloseOpensLogicOnly()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWidget> creating = window.CreateAsync();
            CloseParent();
            _fixture.Loader.Deliver();
            UIProbeWidget child = creating.GetAwaiter().GetResult();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Effective, Is.False);
            Assert.That(child.Registrations, Is.Zero);
            _fixture.Verify();
        }

        [Test]
        public void CreatedUI_ParentDestroyReturnsNull()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWidget> creating = window.CreateAsync();
            DestroyParent();
            _fixture.Loader.Deliver();
            Assert.That(creating.GetAwaiter().GetResult(), Is.Null);
            _fixture.Verify();
        }

        [Test]
        public void CreatedUI_TokenCancelCompletesAfterResourceArrives()
        {
            UIProbeWindow window = _fixture.Open();
            using var cancellation = new CancellationTokenSource();
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWidget> creating = window.CreateAsync(token: cancellation.Token);
            cancellation.Cancel();
            Assert.That(creating.Status.IsCompleted(), Is.False);
            _fixture.Loader.Deliver();
            Assert.That(creating.GetAwaiter().GetResult(), Is.Null);
            Assert.That(window.FirstChild, Is.Null);
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void Initializing_CloseCancelsPendingOpen(bool visible)
        {
            UIProbeWidget.Initializing = widget => widget.Close();
            UIProbeWidget child = _fixture.Open().Create(visible);
            Assert.That(child.Opens + child.Closes, Is.Zero);
            Assert.That(child.State, Is.EqualTo(visible ? UIState.Closed : UIState.Initialized));
            _fixture.Verify();
        }

        [TestCase(false)] [TestCase(true)]
        public void Initializing_DestroyReturnsNull(bool destroyNow)
        {
            UIProbeWidget created = null;
            UIProbeWidget.Initializing = widget =>
            {
                created = widget;
                if (destroyNow) widget.DestroyNow();
                else widget.Destroy().Forget();
            };
            Assert.That(_fixture.Open().Create(), Is.Null);
            Assert.That(created.Destroys, Is.EqualTo(1));
            Assert.That(created.Opens + created.Closes, Is.Zero);
            _fixture.Verify();
        }

        [Test]
        public void Initialized_Operations()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            child.Close();
            Assert.That(child.State, Is.EqualTo(UIState.Initialized));
            child.Destroy().GetAwaiter().GetResult();
            Assert.That(child.Closes, Is.Zero);
            Assert.That(child.Destroys, Is.EqualTo(1));
            UIProbeWidget other = window.Create(false);
            CloseParent();
            Assert.That(other.State, Is.EqualTo(UIState.Initialized));
            DestroyParent();
            Assert.That(other.Closes, Is.Zero);
            Assert.That(other.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void Opening_OpenCoalescesAndKeepsLatestArguments()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            UIGateTransition gate = Opening(child);
            child.Open("latest");
            child.Open();
            Assert.That(child.Refreshes, Is.Zero);
            gate.Finish();
            Assert.That(child.Refreshes, Is.EqualTo(1));
            Assert.That(child.Argument, Is.EqualTo("latest"));
            _fixture.Verify();
        }

        [Test]
        public void Opening_ParentCloseKeepsStateAndTransition()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            UIGateTransition gate = Opening(child);
            CloseParent();
            Assert.That(child.State, Is.EqualTo(UIState.Opening));
            Assert.That(child.Effective, Is.False);
            Assert.That(child.IsOpen, Is.True);
            Assert.That(child.IsVisible, Is.False);
            _fixture.Verify();
            gate.Finish();
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
            Assert.That(child.Closes, Is.Zero);
            _fixture.Verify();
        }

        [Test]
        public void Opening_DestroyVariants()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(false);
            UIGateTransition gateA = Opening(a);
            UniTask destroying = a.Destroy();
            Assert.That(a.State, Is.EqualTo(UIState.Closing));
            Assert.That(a.Closes, Is.EqualTo(1));
            gateA.Finish();
            destroying.GetAwaiter().GetResult();
            Assert.That(a.Destroys, Is.EqualTo(1));
            UIProbeWidget b = window.Create(false);
            Opening(b);
            b.DestroyNow();
            Assert.That(b.Closes, Is.EqualTo(1));
            Assert.That(b.Destroys, Is.EqualTo(1));
            UIProbeWidget c = window.Create(false);
            Opening(c);
            DestroyParent();
            Assert.That(c.Closes, Is.EqualTo(1));
            Assert.That(c.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void Opened_OpenWithAndWithoutArguments()
        {
            UIProbeWidget child = _fixture.Open().Create();
            child.Open("data");
            Assert.That(child.Refreshes, Is.EqualTo(1));
            child.Open();
            Assert.That(child.Refreshes, Is.EqualTo(2));
            Assert.That(child.Argument, Is.EqualTo("data"));
            _fixture.Verify();
        }

        [Test]
        public void Opened_ParentCloseSuspendsAndReopenResumes()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            CloseParent();
            Assert.That(child.Closes, Is.Zero);
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.Events + child.Updates, Is.Zero);
            _fixture.Open();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.Events, Is.EqualTo(1));
            Assert.That(child.Updates, Is.EqualTo(1));
            Assert.That(child.Registrations, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void Closing_OpenIsIgnoredAndCloseDoesNotRepeat()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            UIGateTransition gate = Closing(child);
            child.Open("ignored");
            child.Close();
            Assert.That(child.Argument, Is.EqualTo("first"));
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            gate.Finish();
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
            _fixture.Verify();
        }

        [Test]
        public void Closing_DestroyUpgradesAfterTransition()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            UIGateTransition gate = Closing(child);
            UniTask destroying = child.Destroy();
            Assert.That(child.Destroys, Is.Zero);
            gate.Finish();
            destroying.GetAwaiter().GetResult();
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void Closing_ParentCloseContinuesAndParentDestroyIsImmediate()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            UIGateTransition gate = Closing(child);
            CloseParent();
            Assert.That(child.State, Is.EqualTo(UIState.Closing));
            gate.Finish();
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
            UIProbeWidget other = _fixture.Open().Create(false);
            Closing(other);
            DestroyParent();
            Assert.That(other.Closes, Is.EqualTo(1));
            Assert.That(other.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void Closed_OpenReopensAndDestroyDoesNotCloseAgain()
        {
            UIProbeWidget child = _fixture.Open().Create();
            child.Close();
            child.Close();
            Assert.That(child.Closes, Is.EqualTo(1));
            child.Open();
            Assert.That(child.Opens, Is.EqualTo(2));
            child.Close();
            child.Destroy().GetAwaiter().GetResult();
            Assert.That(child.Closes, Is.EqualTo(2));
            Assert.That(child.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void Destroyed_AllOperationsAreIgnored()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            child.DestroyNow();
            child.Open("ignored");
            child.Close();
            child.Destroy().GetAwaiter().GetResult();
            child.DestroyNow();
            window.RemoveWidget(child).GetAwaiter().GetResult();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [Test]
        public void UnderClosedParent_TransitionsSnapWithoutPlaying()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            var source = new UITickGateTransition();
            child.Holder.SetTransition(source);
            CloseParent();
            child.Open();
            Assert.That(source.Begins, Is.Zero);
            Assert.That(source.LastSnap, Is.True);
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
            child.Close();
            Assert.That(source.Begins, Is.Zero);
            Assert.That(source.LastSnap, Is.False);
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
            _fixture.Verify();
        }

        [Test]
        public void ChildList_ConsecutiveRemovalAndAppend()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(), b = window.Create(), c = window.Create(), d = window.Create();
            a.Updating = () =>
            {
                a.Updating = null;
                a.DestroyNow();
                b.DestroyNow();
            };
            _fixture.Tick();
            Assert.That(b.Updates, Is.Zero);
            Assert.That(c.Updates, Is.EqualTo(1));
            Assert.That(d.Updates, Is.EqualTo(1));
            d.DestroyNow();
            UIProbeWidget e = window.Create();
            CloseParent();
            _fixture.Open();
            _fixture.Tick();
            Assert.That(c.Updates, Is.EqualTo(2));
            Assert.That(e.Updates, Is.EqualTo(1));
            Assert.That(window.FirstChild, Is.SameAs(c));
            _fixture.Verify();
        }

        [Test]
        public void B1_SiblingDestroyAndParentReopenDuringUpdate()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget parent = window.Create();
            UIProbeWidget a = parent.Create(), b = parent.Create(), c = parent.Create();
            a.Updating = () =>
            {
                a.Updating = null;
                b.DestroyNow();
                parent.Close();
                parent.Open();
            };
            _fixture.Tick();
            _fixture.Tick();
            Assert.That(b.Destroys, Is.EqualTo(1));
            Assert.That(c.Updates, Is.GreaterThanOrEqualTo(1));
            _fixture.Verify();
        }
    }
}
#endif
