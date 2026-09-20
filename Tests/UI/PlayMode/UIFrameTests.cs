using System.Collections;
using System.Collections.Generic;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlicizaX.UI.Tests
{
    public sealed class UIFrameTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [UnityTest]
        public IEnumerator PresetAnimationAndParentCacheKeepIndependentChildState()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Bound = holder =>
            {
                if (holder is UIWidgetHolder) holder.SetTransition(holder.gameObject.AddComponent<UIPresetTransition>());
            };
            UIProbeWidget child = window.Create(false);
            child.Open();
            yield return UIFixture.Wait(child.AwaitTransition());
            Assert.That(child.Holder.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(0.001f));
            child.Close();
            var closing = child.AwaitTransition();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            yield return UIFixture.Wait(closing);
            Assert.That(child.IsOpen, Is.False);
            Assert.That(child.Holder.GetComponent<CanvasGroup>().alpha, Is.Zero);
            _fixture.Open();
            Assert.That(child.IsVisible, Is.False);
            child.Open();
            var opening = child.AwaitTransition();
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            yield return UIFixture.Wait(opening);
            Assert.That(child.IsOpen, Is.True);
            Assert.That(child.IsVisible, Is.False);
            Assert.That(child.Holder.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            _fixture.Open();
            Assert.That(child.Holder.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(child.Opens, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ClosingWidgetBlocksGraphicRaycastAndHiddenChildOpenCannotRestoreIt()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget a = window.Create();
            UIProbeWidget b = a.Create();
            var eventObject = new GameObject("test events", typeof(EventSystem));
            eventObject.transform.SetParent(_fixture.Root.transform);
            var buttonObject = new GameObject("button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(b.Holder.transform, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200, 200);
            yield return null;
            Canvas.ForceUpdateCanvases();
            GraphicRaycaster raycaster = window.Holder.GetComponent<GraphicRaycaster>();
            var pointer = new PointerEventData(eventObject.GetComponent<EventSystem>())
            {
                position = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera, rect.position),
            };
            var hits = new List<RaycastResult>();
            raycaster.Raycast(pointer, hits);
            Assert.That(hits.Exists(hit => hit.gameObject == buttonObject), Is.True,
                $"point={pointer.position}, rect={rect.position}, camera={raycaster.eventCamera}, depth={buttonObject.GetComponent<Image>().depth}");
            var gate = new UIGateTransition();
            a.Holder.SetTransition(gate);
            a.Close();
            hits.Clear();
            raycaster.Raycast(pointer, hits);
            Assert.That(hits.Exists(hit => hit.gameObject == buttonObject), Is.False);
            b.Close();
            b.Open();
            hits.Clear();
            raycaster.Raycast(pointer, hits);
            Assert.That(hits.Exists(hit => hit.gameObject == buttonObject), Is.False);
            gate.Finish();
            a.Open();
            gate.Finish();
            hits.Clear();
            raycaster.Raycast(pointer, hits);
            Assert.That(hits.Exists(hit => hit.gameObject == buttonObject), Is.True);
            _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            Assert.That(raycaster.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator DestroyDuringLoadDisposesLateWidgetAndSettlesCreation()
        {
            UIProbeWindow window = _fixture.Open();
            _fixture.Loader.Deferred = true;
            var creation = window.CreateAsync();
            window.DestroyNow();
            Assert.That(_fixture.Loader.PendingToken.IsCancellationRequested, Is.True);
            GameObject late = _fixture.Loader.Deliver();
            Assert.That(creation.GetAwaiter().GetResult(), Is.Null);
            yield return null;
            Assert.That(late == null, Is.True);
        }

        [UnityTest]
        public IEnumerator ZeroCacheDestroysSubtreeAfterWindowClose()
        {
            var window = _fixture.Service.ShowUISync<UIZeroCacheWindow>();
            UIProbeWidget child = window.Create();
            UIHolderObjectBase holder = child.Holder;
            yield return UIFixture.Wait(_fixture.Service.CloseUI<UIZeroCacheWindow>());
            yield return null;
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(child.Closes, Is.EqualTo(1));
            Assert.That(holder == null, Is.True);
            Assert.That(_fixture.Service.GetUI<UIZeroCacheWindow>(), Is.Null);
        }
    }
}
