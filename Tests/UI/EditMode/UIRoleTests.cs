using System;
using System.Text.RegularExpressions;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Tests
{
    public sealed class UIRoleTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [Test]
        public void WindowAddsOnlyItsRequiredComponents()
        {
            _fixture.Loader.Bound = holder =>
            {
                if (!(holder is UIWindowHolder)) return;
                Object.DestroyImmediate(holder.GetComponent<GraphicRaycaster>());
                Object.DestroyImmediate(holder.GetComponent<Canvas>());
            };
            UIProbeWindow window = _fixture.Open();
            Assert.That(window.Holder.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(window.Holder.GetComponent<GraphicRaycaster>().enabled, Is.True);
            Assert.That(window.Holder.GetComponent<CanvasGroup>(), Is.Null);
            UIProbeWidget child = window.Create();
            Assert.That(child.Holder.GetComponent<CanvasGroup>(), Is.Not.Null);
            Assert.That(child.Holder.GetComponent<Canvas>(), Is.Null);
        }

        [Test]
        public void BorrowedHolderIsDestroyedByDefault()
        {
            UIProbeWindow window = _fixture.Open();
            var obj = new GameObject("borrowed", typeof(RectTransform), typeof(UIWidgetHolder));
            obj.transform.SetParent(window.Holder.transform, false);
            UIProbeWidget child = window.Borrow(obj.GetComponent<UIWidgetHolder>());
            child.DestroyNow();
            Assert.That(obj == null, Is.True);
        }

        [Test]
        public void MissingHolderReturnsFailureAndDisposesResource()
        {
            _fixture.Loader.Bound = holder => Object.DestroyImmediate(holder);
            LogAssert.Expect(LogType.Error, new Regex("missing holder"));
            Assert.That(_fixture.Open(), Is.Null);
            Assert.That(_fixture.Loader.Objects[0] == null, Is.True);
        }

        [Test]
        public void InvalidTypeEntryPointsReturnFailure()
        {
            LogAssert.Expect(LogType.Error, new Regex("Unknown UI type"));
            Assert.That(_fixture.Service.ShowUI("NoSuchUI").GetAwaiter().GetResult(), Is.Null);
            LogAssert.Expect(LogType.Error, new Regex("invalid window type"));
            Assert.That(_fixture.Service.ShowUI(typeof(UIProbeWidget).TypeHandle).GetAwaiter().GetResult(), Is.Null);
            LogAssert.Expect(LogType.Error, new Regex("type handle is empty"));
            Assert.That(_fixture.Service.ShowUI(default(RuntimeTypeHandle)).GetAwaiter().GetResult(), Is.Null);
        }

        [Test]
        public void ClosingOneBranchDuringUpdateDoesNotSkipItsSibling()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(), b = window.Create(), c = window.Create();
            a.Updating = () => b.DestroyNow();
            _fixture.Tick();
            Assert.That(a.Updates, Is.EqualTo(1));
            Assert.That(b.Updates, Is.Zero);
            Assert.That(c.Updates, Is.EqualTo(1));
        }

        [Test]
        public void TabSwitchSuspendsPreviousSubtreeAndCachePreservesSelection()
        {
            var window = _fixture.Service.ShowUISync<UIProbeTabWindow>();
            var a = (UIProbeWidget)window.SwitchTab(0).GetAwaiter().GetResult();
            UIProbeWidget child = a.Create();
            UIWidget b = window.SwitchTab(1).GetAwaiter().GetResult();
            Assert.That(a.IsOpen, Is.False);
            Assert.That(child.IsOpen, Is.True);
            UIFixture.Publish();
            Assert.That(child.Events, Is.Zero);
            _fixture.Service.CloseUI<UIProbeTabWindow>().GetAwaiter().GetResult();
            _fixture.Service.ShowUISync<UIProbeTabWindow>();
            Assert.That(a.IsOpen, Is.False);
            Assert.That(b.IsOpen, Is.True);
            Assert.That(window.SwitchTab(0).GetAwaiter().GetResult(), Is.SameAs(a));
            UIFixture.Publish();
            Assert.That(child.Events, Is.EqualTo(1));
        }

        [Test]
        public void FailedNavigationPreservesCurrentWindowAndHistory()
        {
            Assert.That(_fixture.Service.NavigateTo<UIProbeWindow>().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
            _fixture.Loader.Fail = true;
            LogAssert.Expect(LogType.Error, new Regex("could not be loaded"));
            Assert.That(_fixture.Service.NavigateTo<UISecondWindow>().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.OpenFailed));
            Assert.That(_fixture.Service.Current, Is.EqualTo(typeof(UIProbeWindow)));
            Assert.That(_fixture.Service.CanBack, Is.False);
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.True);
        }

        [Test]
        public void NavigationBackRestoresCachedWindowAndChildState()
        {
            _fixture.Service.NavigateTo<UIProbeWindow>().GetAwaiter().GetResult();
            UIProbeWindow a = _fixture.Service.GetUI<UIProbeWindow>();
            UIProbeWidget child = a.Create();
            _fixture.Service.NavigateTo<UISecondWindow>().GetAwaiter().GetResult();
            Assert.That(child.IsOpen, Is.True);
            Assert.That(child.IsVisible, Is.False);
            Assert.That(_fixture.Service.Back().GetAwaiter().GetResult().Status, Is.EqualTo(UIRouteStatus.Success));
            Assert.That(child.IsVisible, Is.True);
            Assert.That(child.Opens, Is.EqualTo(1));
        }
    }
}
