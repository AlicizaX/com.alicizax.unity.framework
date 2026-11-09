#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Tests
{
    public abstract class UIServiceApiCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private IUIDebugService Debug => _fixture.Service;

        private static int SortingOrder(UIWindow window) => window.Holder.GetComponent<Canvas>().sortingOrder;

        [Test]
        public void ReopeningAnOlderWindowBringsItToTheFrontAndClosingReindexes()
        {
            UIProbeWindow probe = _fixture.Open();
            UISecondWindow second = _fixture.Service.ShowUISync<UISecondWindow>();
            UIThirdWindow third = _fixture.Service.ShowUISync<UIThirdWindow>();
            Assert.That(new[] { probe.LayerIndex, second.LayerIndex, third.LayerIndex }, Is.EqualTo(new[] { 0, 1, 2 }));
            _fixture.Open();
            Assert.That(new[] { second.LayerIndex, third.LayerIndex, probe.LayerIndex }, Is.EqualTo(new[] { 0, 1, 2 }));
            _fixture.Service.CloseUI<UIThirdWindow>();
            Assert.That(third.LayerIndex, Is.EqualTo(-1));
            Assert.That(new[] { second.LayerIndex, probe.LayerIndex }, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(SortingOrder(second), Is.EqualTo(second.Depth));
            Assert.That(SortingOrder(probe), Is.EqualTo(probe.Depth));
            Assert.That(probe.Depth, Is.GreaterThan(second.Depth));
            _fixture.Service.ShowUISync<UIThirdWindow>();
            Assert.That(third.LayerIndex, Is.EqualTo(2));
            Assert.That(SortingOrder(third), Is.EqualTo(third.Depth));
        }

        [Test]
        public void HigherLayerStaysAboveRegardlessOfOpenOrder()
        {
            UIPopupWindow popup = _fixture.Service.ShowUISync<UIPopupWindow>();
            UIProbeWindow probe = _fixture.Open();
            Assert.That(popup.LayerIndex, Is.Zero);
            Assert.That(probe.LayerIndex, Is.Zero);
            Assert.That(popup.Depth, Is.GreaterThan(probe.Depth));
            Assert.That(SortingOrder(popup), Is.GreaterThan(SortingOrder(probe)));
        }

        [Test]
        public void TopVisibleHolderPrefersHigherLayersThenTheTopmostAndSkipsClosing()
        {
            UIProbeWindow probe = _fixture.Open();
            UISecondWindow second = _fixture.Service.ShowUISync<UISecondWindow>();
            UIPopupWindow popup = _fixture.Service.ShowUISync<UIPopupWindow>();
            Assert.That(_fixture.Service.TryGetTopVisibleHolder(null, out UIHolderObjectBase holder), Is.True);
            Assert.That(holder, Is.SameAs(popup.Holder));
            _fixture.Gate(popup);
            _fixture.Service.CloseUI<UIPopupWindow>();
            Assert.That(_fixture.Service.TryGetTopVisibleHolder(null, out holder), Is.True);
            Assert.That(holder, Is.SameAs(second.Holder));
            Assert.That(_fixture.Service.TryGetTopVisibleHolder(h => h != second.Holder, out holder), Is.True);
            Assert.That(holder, Is.SameAs(probe.Holder));
            Assert.That(_fixture.Service.TryGetTopVisibleHolder(_ => false, out holder), Is.False);
            Assert.That(holder, Is.Null);
        }

        [UnityTest]
        public IEnumerator CloseTopClosesTheTopmostMatchingOpenWindow()
        {
            UIProbeWindow probe = _fixture.Open();
            UISecondWindow second = _fixture.Service.ShowUISync<UISecondWindow>();
            UIPopupWindow popup = _fixture.Service.ShowUISync<UIPopupWindow>();
            RuntimeTypeHandle popupHandle = typeof(UIPopupWindow).TypeHandle;
            bool closed = false;
            yield return _fixture.Wait(_fixture.Service.TryCloseTopAsync(h => !h.Equals(popupHandle)), value => closed = value);
            Assert.That(closed, Is.True);
            Assert.That(second.State, Is.EqualTo(UIState.Closed));
            Assert.That(popup.State, Is.EqualTo(UIState.Opened));
            yield return _fixture.Wait(_fixture.Service.TryCloseTopAsync(_ => true), value => closed = value);
            Assert.That(popup.State, Is.EqualTo(UIState.Closed));
            yield return _fixture.Wait(_fixture.Service.TryCloseTopAsync(h => h.Equals(typeof(UIThirdWindow).TypeHandle)), value => closed = value);
            Assert.That(closed, Is.False);
            yield return _fixture.Wait(_fixture.Service.TryCloseTopAsync(null), value => closed = value);
            Assert.That(closed, Is.False);
            yield return _fixture.Wait(_fixture.Service.TryCloseTopAsync(_ => true, force: true), value => closed = value);
            Assert.That(closed, Is.True);
            Assert.That(probe.State, Is.EqualTo(UIState.Destroyed));
        }

        [Test]
        public void BlockShowsRetimesAndForceExitHides()
        {
            var info = new UIServiceDebugInfo();
            _fixture.Service.SetUIBlock(10f);
            Debug.FillServiceDebugInfo(info);
            Assert.That(info.BlockActive, Is.True);
            Assert.That(info.BlockRemaining, Is.GreaterThan(9f));
            _fixture.Service.SetUIBlock(20f);
            Debug.FillServiceDebugInfo(info);
            Assert.That(info.BlockRemaining, Is.GreaterThan(19f));
            _fixture.Service.ForceExitBlock();
            Debug.FillServiceDebugInfo(info);
            Assert.That(info.BlockActive, Is.False);
            Assert.That(info.BlockRemaining, Is.Zero);
        }

        [Test]
        public void BlockAfterShutdownIsRejected()
        {
            AppServices.Shutdown();
            LogAssert.Expect(LogType.Error, new Regex("not running"));
            _fixture.Service.SetUIBlock(1f);
            _fixture.Service.ForceExitBlock();
        }

        [Test]
        public void GetLayerReturnsTheLayerRootAndRejectsInvalidLayers()
        {
            RectTransform layer = _fixture.Service.GetLayer(UILayer.UI);
            Assert.That(layer, Is.Not.Null);
            Assert.That(_fixture.Open().Holder.transform.parent, Is.SameAs(layer));
            Assert.That(_fixture.Service.ShowUISync<UIPopupWindow>().Holder.transform.parent, Is.SameAs(_fixture.Service.GetLayer(UILayer.Popup)));
            LogAssert.Expect(LogType.Error, new Regex("Invalid layer"));
            Assert.That(_fixture.Service.GetLayer(UILayer.All), Is.Null);
        }

        [UnityTest]
        public IEnumerator ShowByNameAndByHandleReachTheSameWindow()
        {
            UIWindow byName = null, byHandle = null;
            yield return _fixture.Wait(_fixture.Service.ShowUI(typeof(UIProbeWindow).FullName), value => byName = value);
            yield return _fixture.Wait(_fixture.Service.ShowUI(typeof(UIProbeWindow).TypeHandle, "handle"), value => byHandle = value);
            Assert.That(byName, Is.InstanceOf<UIProbeWindow>());
            Assert.That(byHandle, Is.SameAs(byName));
            Assert.That(((UIProbeWindow)byName).Argument, Is.EqualTo("handle"));
            _fixture.Service.CloseUI(typeof(UIProbeWindow).TypeHandle);
            Assert.That(byName.State, Is.EqualTo(UIState.Closed));
        }

        [UnityTest]
        public IEnumerator ShowWithInvalidTypesReturnsNull()
        {
            UIWindow result = null;
            LogAssert.Expect(LogType.Error, new Regex("Unknown UI type"));
            yield return _fixture.Wait(_fixture.Service.ShowUI("No.Such.Window"), value => result = value);
            Assert.That(result, Is.Null);
            LogAssert.Expect(LogType.Error, new Regex("must be a window"));
            yield return _fixture.Wait(_fixture.Service.ShowUI(typeof(UIProbeWidget).FullName), value => result = value);
            Assert.That(result, Is.Null);
            LogAssert.Expect(LogType.Error, new Regex("Unknown UI type handle"));
            yield return _fixture.Wait(_fixture.Service.ShowUI(default(RuntimeTypeHandle)), value => result = value);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ShowWithAnAlreadyCancelledTokenDoesNotLoad()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(_fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token).GetAwaiter().GetResult(), Is.Null);
            Assert.That(_fixture.Loader.Loads, Is.Zero);
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator QueriesReflectEveryState()
        {
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWindow> show = _fixture.Service.ShowUI<UIProbeWindow>();
            AssertQueries(open: false, window: null);
            _fixture.Loader.Deliver();
            UIProbeWindow window = null;
            yield return _fixture.Wait(show, value => window = value);
            AssertQueries(open: true, window: window);
            UIGateTransition gate = _fixture.Gate(window);
            _fixture.Service.CloseUI<UIProbeWindow>();
            AssertQueries(open: false, window: null);
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            AssertQueries(open: true, window: window);
            gate.Finish();
            _fixture.Service.CloseUI<UIProbeWindow>();
            gate.Finish();
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            AssertQueries(open: false, window: window);
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            AssertQueries(open: false, window: null);
        }

        private void AssertQueries(bool open, UIProbeWindow window)
        {
            Assert.That(_fixture.Service.IsOpen<UIProbeWindow>(), Is.EqualTo(open));
            Assert.That(_fixture.Service.IsOpen(typeof(UIProbeWindow).TypeHandle), Is.EqualTo(open));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.SameAs(window));
        }

        [UnityTest]
        public IEnumerator WidgetByTypeNameIsCreatedAndWindowTypeIsRejected()
        {
            UIProbeWindow window = _fixture.Open();
            UIWidget widget = window.CreateByName(typeof(UIProbeWidget).FullName);
            Assert.That(widget, Is.InstanceOf<UIProbeWidget>());
            Assert.That(widget.State, Is.EqualTo(UIState.Opened));
            UIWidget loaded = null;
            yield return _fixture.Wait(window.CreateByNameAsync(typeof(UISecondWidget).FullName), value => loaded = value);
            Assert.That(loaded, Is.InstanceOf<UISecondWidget>());
            LogAssert.Expect(LogType.Error, new Regex("must be a Widget"));
            Assert.That(window.CreateByName(typeof(UISecondWindow).FullName), Is.Null);
        }

        [Test]
        public void DestroyCancellationTokenIsCancelledOnDestroy()
        {
            UIProbeWindow window = _fixture.Open();
            CancellationToken token = window.DestroyCancellationToken;
            bool cancelled = false;
            token.Register(() => cancelled = true);
            Assert.That(token.IsCancellationRequested, Is.False);
            _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            Assert.That(cancelled, Is.True);
            Assert.That(window.DestroyCancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public void SetTransitionNullRestoresTheSerializedDefault()
        {
            UIProbeWindow window = _fixture.Open();
            var preset = window.Holder.gameObject.AddComponent<UIPresetTransition>();
            typeof(UIHolderObjectBase).GetField("_defaultTransition", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(window.Holder, preset);
            Assert.That(window.Holder.UITransition, Is.SameAs(preset));
            UIGateTransition gate = _fixture.Gate(window);
            Assert.That(window.Holder.UITransition, Is.SameAs(gate));
            window.Holder.SetTransition(null);
            Assert.That(window.Holder.UITransition, Is.SameAs(preset));
        }

        [Test]
        public void InitializeRejectsInvalidSetups()
        {
            var service = new UIService();
            LogAssert.Expect(LogType.Error, new Regex("UI root is missing"));
            Assert.That(service.Initialize(null, true), Is.False);
            var noCanvas = new GameObject("NoCanvas");
            var noCamera = new GameObject("NoCamera", typeof(RectTransform), typeof(Canvas));
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("must contain a Canvas"));
                Assert.That(service.Initialize(noCanvas.transform, true), Is.False);
                LogAssert.Expect(LogType.Error, new Regex("worldCamera"));
                Assert.That(service.Initialize(noCamera.transform, true), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(noCanvas);
                Object.DestroyImmediate(noCamera);
            }
            LogAssert.Expect(LogType.Error, new Regex("already initialized"));
            Assert.That(_fixture.Service.Initialize(_fixture.Root.transform, true), Is.False);
        }
    }
}
#endif
