#if UNITY_EDITOR
using System.Collections;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Tests
{
    public sealed class UIHolderLostTests
    {
        private sealed class UIBindProbe : MonoBehaviour
        {
            internal static System.Action Changed;
            private void OnCanvasHierarchyChanged() => Changed?.Invoke();
            private void OnCanvasGroupChanged() => Changed?.Invoke();
        }

        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown()
        {
            UIBindProbe.Changed = null;
            _fixture?.Dispose();
        }

        [UnityTest]
        public IEnumerator B4_ExternallyDestroyedHolderReleasesRecord()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            int destroyEvents = 0;
            window.Holder.OnWindowDestroyEvent += () => destroyEvents++;
            Object.Destroy(window.Holder.gameObject);
            yield return null;
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(window.Destroys, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(destroyEvents, Is.EqualTo(1));
            Assert.That(_fixture.Service.FindRecord(typeof(UIProbeWindow).TypeHandle).View, Is.Null);
            _fixture.Verify();
            UIProbeWindow next = _fixture.Open();
            Assert.That(next, Is.Not.SameAs(window));
            Assert.That(next.IsOpen, Is.True);
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator DuringOpeningTransitionWindowIsReleased()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            var source = new UITickGateTransition();
            window.Holder.SetTransition(source);
            _fixture.Open();
            UniTask waiting = window.AwaitTransition();
            Object.DestroyImmediate(window.Holder.gameObject);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(waiting.Status.IsCompleted(), Is.True);
            Assert.That(_fixture.Service.TransitionViews.Count, Is.Zero);
            _fixture.Tick();
            yield return null;
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator DuringClosingTransitionWindowIsReleased()
        {
            UIProbeWindow window = _fixture.Open();
            window.Holder.SetTransition(new UITickGateTransition());
            UniTask closing = _fixture.Service.CloseUI<UIProbeWindow>();
            Object.DestroyImmediate(window.Holder.gameObject);
            Assert.That(closing.Status.IsCompleted(), Is.True);
            Assert.That(window.Closes, Is.EqualTo(1));
            Assert.That(window.Destroys, Is.EqualTo(1));
            yield return null;
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator CachedWindowRemovesCacheEntry()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Object.DestroyImmediate(window.Holder.gameObject);
            yield return null;
            Assert.That(window.Destroys, Is.EqualTo(1));
            Assert.That(_fixture.Service.FindRecord(typeof(UIProbeWindow).TypeHandle).IsCached, Is.False);
            Assert.That(_fixture.Open(), Is.Not.SameAs(window));
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator InsideInitializeDestroysWithoutMissingReference()
        {
            UIProbeWindow created = null;
            UIProbeWindow.Initializing = window =>
            {
                created = window;
                Object.DestroyImmediate(window.Holder.gameObject);
            };
            Assert.That(_fixture.Open(), Is.Null);
            yield return null;
            Assert.That(created.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(created.Destroys, Is.EqualTo(1));
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator WidgetHolderLostOnlyRemovesWidget()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create(), b = window.Create();
            Object.DestroyImmediate(a.Holder.gameObject);
            yield return null;
            Assert.That(a.Destroys, Is.EqualTo(1));
            Assert.That(window.FirstChild, Is.SameAs(b));
            Assert.That(window.IsOpen, Is.True);
            _fixture.Tick();
            Assert.That(b.Updates, Is.EqualTo(1));
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator BorrowedHolderLostReleasesWidgetOnly()
        {
            UIProbeWindow window = _fixture.Open();
            var obj = new GameObject("borrowed", typeof(RectTransform), typeof(UIWidgetHolder));
            obj.transform.SetParent(window.Holder.transform, false);
            UIProbeWidget child = window.Borrow(obj.GetComponent<UIWidgetHolder>(), false);
            Object.DestroyImmediate(obj);
            yield return null;
            Assert.That(child.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(window.IsOpen, Is.True);
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator DeferredDestroyKeepsViewUsableUntilEndOfFrame()
        {
            UIProbeWindow window = _fixture.Open();
            Object.Destroy(window.Holder.gameObject);
            Assert.That(window.IsOpen, Is.True);
            _fixture.Tick();
            Assert.That(window.Updates, Is.EqualTo(1));
            yield return null;
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
        }

        [UnityTest]
        public IEnumerator NeverActivatedHolderIsNotNotified()
        {
            UIProbeWindow window = _fixture.Open();
            var parent = new GameObject("inactive-parent", typeof(RectTransform));
            parent.SetActive(false);
            parent.transform.SetParent(window.Holder.transform, false);
            var obj = new GameObject("inactive-holder", typeof(RectTransform));
            obj.transform.SetParent(parent.transform, false);
            var holder = obj.AddComponent<UIWidgetHolder>();
            UIProbeWidget child = window.Borrow(holder, false);
            child.Close();
            var source = new UITickGateTransition();
            holder.SetTransition(source);
            child.Open();
            Assert.That(_fixture.Service.TransitionViews.Count, Is.EqualTo(1));
            Object.DestroyImmediate(parent);
            yield return null;
            Assert.That(child.State, Is.Not.EqualTo(UIState.Destroyed));
            Assert.That(child.Holder == null, Is.True);
            child.LoseHolder();
            Assert.That(child.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(_fixture.Service.TransitionViews.Count, Is.Zero);
            _fixture.Tick();
        }

        [UnityTest]
        public IEnumerator HiddenWindowMatchesInputLayerContract()
        {
            UIProbeWindow window = _fixture.Open();
            Canvas canvas = window.Holder.GetComponent<Canvas>();
            Assert.That(canvas.enabled, Is.True);
            Assert.That(window.Holder.gameObject.layer, Is.EqualTo(UIComponent.UIShowLayer));
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(canvas.enabled, Is.False);
            Assert.That(window.Holder.gameObject.layer, Is.EqualTo(UIComponent.UIHideLayer));
            Assert.That(window.Holder.GetComponent<GraphicRaycaster>().enabled, Is.False);
            Assert.That(window.Holder.gameObject.activeInHierarchy, Is.True);
            Transform parent = window.Holder.transform.parent;
            _fixture.Open();
            Assert.That(window.Holder.transform.parent, Is.SameAs(parent));
            Assert.That(canvas.enabled, Is.True);
            Assert.That(window.Holder.gameObject.layer, Is.EqualTo(UIComponent.UIShowLayer));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CanvasStaysEnabledDuringInitializeForLayoutQueries()
        {
            bool enabledInInitialize = false;
            int layerInInitialize = -1;
            UIProbeWindow.Initializing = window =>
            {
                enabledInInitialize = window.Holder.GetComponent<Canvas>().enabled;
                layerInInitialize = window.Holder.gameObject.layer;
            };
            UIProbeWindow opened = _fixture.Open();
            Assert.That(enabledInInitialize, Is.True);
            Assert.That(layerInInitialize, Is.EqualTo(UIComponent.UIHideLayer));
            Assert.That(opened.Holder.GetComponent<Canvas>().enabled, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InitializeCloseDisablesCanvas()
        {
            UIProbeWindow.Initializing = window => _fixture.Service.CloseUI<UIProbeWindow>().Forget();
            Assert.That(_fixture.Open(), Is.Null);
            UIProbeWindow.Initializing = null;
            UIWindow window2 = _fixture.Service.GetWindow(typeof(UIProbeWindow).TypeHandle);
            Assert.That(window2.Holder.GetComponent<Canvas>().enabled, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NestedCanvasFollowsHiddenWindowLayer()
        {
            UIProbeWindow window = _fixture.Open();
            var nested = new GameObject("nested", typeof(RectTransform), typeof(Canvas));
            nested.transform.SetParent(window.Holder.transform, false);
            nested.GetComponent<Canvas>().overrideSorting = true;
            yield return null;
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            yield return null;
            Assert.That(nested.activeInHierarchy, Is.True);
            Assert.That(window.Holder.GetComponent<Canvas>().enabled, Is.False);
            _fixture.Open();
            yield return null;
            Assert.That(window.Holder.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(nested.GetComponent<Canvas>().enabled, Is.True);
        }

        private UIProbeWindow CachedWindowWithProbe()
        {
            UIProbeWindow window = _fixture.Open();
            window.Holder.gameObject.AddComponent<UIBindProbe>();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            return window;
        }

        [UnityTest]
        public IEnumerator CanvasCallbackMayDestroyWindowWhileOpening()
        {
            UIProbeWindow window = CachedWindowWithProbe();
            UIBindProbe.Changed = () =>
            {
                UIBindProbe.Changed = null;
                window.DestroyNow();
            };
            UIProbeWindow opened = _fixture.Open();
            yield return null;
            if (UIBindProbe.Changed == null)
            {
                Assert.That(opened, Is.Null);
                Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            }
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator CanvasCallbackMayDestroyWindowWhileClosing()
        {
            UIProbeWindow window = _fixture.Open();
            window.Holder.gameObject.AddComponent<UIBindProbe>();
            UIBindProbe.Changed = () =>
            {
                UIBindProbe.Changed = null;
                window.DestroyNow();
            };
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            yield return null;
            if (UIBindProbe.Changed == null) Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator CanvasGroupCallbackMayCloseWidgetWhileBecomingEffective()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget child = window.Create();
            child.Holder.gameObject.AddComponent<UIBindProbe>();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            UIBindProbe.Changed = () =>
            {
                UIBindProbe.Changed = null;
                child.Close();
            };
            _fixture.Open();
            yield return null;
            _fixture.Verify();
            _fixture.Tick();
            _fixture.Verify();
        }

        [UnityTest]
        public IEnumerator BindCallbackCloseCancelsAsyncLoad()
        {
            _fixture.Loader.Deferred = true;
            _fixture.Loader.Bound = holder =>
            {
                Object.DestroyImmediate(holder.GetComponent<GraphicRaycaster>());
                Object.DestroyImmediate(holder.GetComponent<Canvas>());
                holder.gameObject.AddComponent<UIBindProbe>();
            };
            UIBindProbe.Changed = () =>
            {
                UIBindProbe.Changed = null;
                _fixture.Service.CloseUI<UIProbeWindow>().Forget();
            };
            UniTask<UIProbeWindow> loading = _fixture.Service.ShowUI<UIProbeWindow>();
            _fixture.Loader.Deliver();
            yield return null;
            UIProbeWindow result = loading.GetAwaiter().GetResult();
            if (UIBindProbe.Changed != null)
            {
                Assert.That(result, Is.Not.Null);
                yield break;
            }
            Assert.That(result, Is.Null);
            Assert.That(_fixture.Service.FindRecord(typeof(UIProbeWindow).TypeHandle).View, Is.Null);
            _fixture.Verify();
        }
    }

    public sealed class UICrossThreadCancelTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [UnityTest]
        public IEnumerator TokenCancelledOnWorkerThreadFinishesOnMainThread()
        {
            using var cancellation = new System.Threading.CancellationTokenSource();
            _fixture.Loader.Deferred = true;
            UniTask<UIProbeWindow> loading = _fixture.Service.ShowUI<UIProbeWindow>(cancellation.Token);
            var worker = new System.Threading.Thread(() => cancellation.Cancel());
            worker.Start();
            worker.Join();
            yield return null;
            yield return null;
            Assert.That(loading.Status.IsCompleted(), Is.True);
            Assert.That(loading.GetAwaiter().GetResult(), Is.Null);
            Assert.That(_fixture.Service.FindRecord(typeof(UIProbeWindow).TypeHandle).View, Is.Null);
            _fixture.Loader.Deliver();
            _fixture.Verify();
        }
    }
}
#endif
