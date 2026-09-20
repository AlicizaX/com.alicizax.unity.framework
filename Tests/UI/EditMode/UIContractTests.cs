using System;
using AlicizaX.UI.Runtime;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    public sealed class UIContractTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [Test]
        public void ParentCachePreservesChildLifecycleAndResumesEvents()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            _fixture.Tick();
            UIFixture.Publish();
            Assert.That(child.Updates, Is.EqualTo(1));
            Assert.That(child.Events, Is.EqualTo(1));
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Tick();
            UIFixture.Publish();
            Assert.That(child.Closes, Is.Zero);
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
            Assert.That(child.Updates, Is.EqualTo(1));
            Assert.That(child.Events, Is.EqualTo(1));
            Assert.That(_fixture.Open(), Is.SameAs(window));
            _fixture.Tick();
            UIFixture.Publish();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Registrations, Is.EqualTo(2));
            Assert.That(child.Updates, Is.EqualTo(2));
            Assert.That(child.Events, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitOpenWhileParentClosedRunsLogicImmediately()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create(false);
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            child.Open();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.State, Is.EqualTo(UIState.Opened));
            Assert.That(child.Registrations, Is.Zero);
            _fixture.Open();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Registrations, Is.EqualTo(1));
        }

        [Test]
        public void NestedWidgetsSuspendWithoutClosingAndRespectTheirOwnState()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            UIProbeWidget grandchild = child.Create();
            UIProbeWidget closed = child.Create(false);
            child.Close();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(grandchild.Closes, Is.Zero);
            Assert.That(grandchild.Events + grandchild.Updates, Is.Zero);
            child.Open();
            Assert.That(grandchild.Opens, Is.EqualTo(1));
            Assert.That(closed.Opens, Is.Zero);
            UIFixture.Publish();
            Assert.That(grandchild.Events, Is.EqualTo(1));
            Assert.That(closed.Events, Is.Zero);
        }

        [Test]
        public void ParentDestroyClosesAndDestroysEachDescendantExactlyOnce()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            UIProbeWidget grandchild = child.Create();
            _fixture.Service.CloseUI<UIProbeWindow>(force: true).GetAwaiter().GetResult();
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(grandchild.Closes, Is.EqualTo(1));
            Assert.That(grandchild.Destroys, Is.EqualTo(1));
            UIFixture.Publish();
            Assert.That(child.Events + grandchild.Events, Is.Zero);
        }

        [Test]
        public void InitializingParentCanCreateAnOpenedWidgetWithoutRegisteringEarly()
        {
            UIProbeWidget child = null;
            int opensDuringInitialize = -1, registrationsDuringInitialize = -1;
            UIProbeWindow.Initializing = window =>
            {
                child = window.Create();
                opensDuringInitialize = child.Opens;
                registrationsDuringInitialize = child.Registrations;
            };
            _fixture.Open();
            Assert.That(opensDuringInitialize, Is.EqualTo(1));
            Assert.That(registrationsDuringInitialize, Is.Zero);
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Registrations, Is.EqualTo(1));
        }

        [Test]
        public void CachedClosedWidgetDoesNotOpenWhenWindowReturns()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            child.Close();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Open();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(child.State, Is.EqualTo(UIState.Closed));
        }

        [Test]
        public void DelayedChildLoadDuringParentCacheOpensOnlyChildLogic()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            var task = window.CreateAsync();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            _fixture.Loader.Deliver();
            UIProbeWidget child = task.GetAwaiter().GetResult();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Registrations, Is.Zero);
            _fixture.Open();
            Assert.That(child.Opens, Is.EqualTo(1));
            Assert.That(child.Registrations, Is.EqualTo(1));
        }
    }
}
