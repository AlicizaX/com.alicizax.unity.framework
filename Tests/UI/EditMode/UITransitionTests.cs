using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.UI.Tests
{
    public sealed class UITransitionTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [Test]
        public void WindowOpeningRestoresEventsBeforeUpdateAndDoesNotReopenChildren()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            Assert.That(_fixture.Service.ShowUI<UIProbeWindow>().GetAwaiter().GetResult(), Is.SameAs(window));
            Assert.That(window.IsOpen && child.IsOpen && child.IsVisible, Is.True);
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.Events, Is.EqualTo(1));
            Assert.That(child.Updates + window.Updates, Is.Zero);
            Assert.That(child.Opens, Is.EqualTo(1));
            gate.Finish();
            _fixture.Tick();
            Assert.That(child.Updates, Is.EqualTo(1));
            Assert.That(window.Updates, Is.EqualTo(1));
        }

        [Test]
        public void OpeningWidgetAllowsAlreadyOpenedDescendantsToUpdate()
        {
            UIProbeWidget a = _fixture.Open().Create();
            UIProbeWidget b = a.Create();
            a.Close();
            var gate = new UIGateTransition();
            a.Holder.SetTransition(gate);
            a.Open();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(a.Events, Is.EqualTo(1));
            Assert.That(b.Events, Is.EqualTo(1));
            Assert.That(a.Updates, Is.Zero);
            Assert.That(b.Updates, Is.EqualTo(1));
            gate.Finish();
        }

        [Test]
        public void RepeatedOpeningCoalescesRefreshAndPreservesEmptyArguments()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Open("first");
            var transition = child.AwaitTransition();
            child.Open("latest");
            child.Open();
            Assert.That(child.Argument, Is.EqualTo("latest"));
            Assert.That(child.Refreshes, Is.Zero);
            Assert.That(transition.GetAwaiter().IsCompleted, Is.False);
            gate.Finish();
            transition.GetAwaiter().GetResult();
            Assert.That(child.Refreshes, Is.EqualTo(1));
            Assert.That(child.Opens, Is.EqualTo(1));
        }

        [Test]
        public void ParentCloseDoesNotCancelRunningChildOpenAnimation()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Open();
            var transition = child.AwaitTransition();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(transition.GetAwaiter().IsCompleted, Is.False);
            Assert.That(child.IsOpen, Is.True);
            Assert.That(child.IsVisible, Is.False);
            gate.Finish();
            transition.GetAwaiter().GetResult();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.Events + child.Updates + child.Closes, Is.Zero);
            Assert.That(child.Holder.GetComponent<CanvasGroup>().interactable, Is.False);
        }

        [Test]
        public void ExplicitTransitionsUnderClosedParentSnapWithoutPlaying()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            child.Open();
            Assert.That(child.IsOpen, Is.True);
            Assert.That(gate.LastSnap, Is.True);
            child.Close();
            Assert.That(child.IsOpen, Is.False);
            Assert.That(gate.LastSnap, Is.False);
            Assert.That(gate.OpenPlays + gate.ClosePlays, Is.Zero);
        }

        [Test]
        public void AwaitTransitionCapturesOneTransitionAndClosingIgnoresOpen()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            child.Open("original");
            var opening = child.AwaitTransition();
            child.Close();
            opening.GetAwaiter().GetResult();
            var closing = child.AwaitTransition();
            child.Open("ignored");
            Assert.That(closing.GetAwaiter().IsCompleted, Is.False);
            Assert.That(child.Argument, Is.EqualTo("original"));
            Assert.That(child.IsVisible, Is.True);
            Assert.That(child.IsOpen, Is.False);
            gate.Finish();
            closing.GetAwaiter().GetResult();
            Assert.That(child.IsVisible, Is.False);
            Assert.That(child.Opens, Is.EqualTo(1));
        }

        [Test]
        public void CloseUIWaitsForWindowAnimationAndSuspendsSubtreeImmediately()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            var gate = new UIGateTransition();
            window.Holder.SetTransition(gate);
            var close = _fixture.Service.CloseUI<UIProbeWindow>();
            Assert.That(close.GetAwaiter().IsCompleted, Is.False);
            Assert.That(window.IsVisible && child.IsVisible, Is.True);
            Assert.That(window.IsOpen, Is.False);
            Assert.That(child.IsOpen, Is.True);
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.Events + child.Updates + child.Closes, Is.Zero);
            gate.Finish();
            close.GetAwaiter().GetResult();
            Assert.That(window.IsVisible || child.IsVisible, Is.False);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.SameAs(window));
        }

        [Test]
        public void HolderAfterShowOccursBeforeOpenAnimation()
        {
            UIProbeWidget child = _fixture.Open().Create(false);
            var gate = new UIGateTransition();
            child.Holder.SetTransition(gate);
            bool openInEvent = false;
            int playsInEvent = -1;
            child.Holder.OnWindowAfterShowEvent += () => { openInEvent = child.IsOpen; playsInEvent = gate.OpenPlays; };
            child.Open();
            Assert.That(openInEvent, Is.True);
            Assert.That(playsInEvent, Is.Zero);
            Assert.That(gate.OpenPlays, Is.EqualTo(1));
            gate.Finish();
        }
    }
}
