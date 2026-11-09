#if UNITY_EDITOR
using System.Collections;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public sealed class UIPresetTransitionTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        private CanvasGroup UsePreset(UIProbeWindow window, float duration, UITransitionPreset kind = UITransitionPreset.Fade)
        {
            GameObject node = window.Holder.gameObject;
            CanvasGroup group = node.AddComponent<CanvasGroup>();
            var preset = node.AddComponent<UIPresetTransition>();
            UIFixture.ConfigurePreset(preset, kind, duration);
            window.Holder.SetTransition(preset);
            return group;
        }

        [UnityTest]
        public IEnumerator EveryPresetReturnsToTheOpenStateAndSnapsWhenAsked([Values] UITransitionPreset kind)
        {
            UIProbeWindow window = _fixture.Open();
            RectTransform rect = window.Holder.RectTransform;
            Vector2 position = rect.anchoredPosition;
            Vector3 scale = rect.localScale;
            CanvasGroup group = UsePreset(window, 0.05f, kind);
            yield return _fixture.Wait(_fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition());
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            _fixture.Open();
            yield return _fixture.Wait(window.AwaitTransition());
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(rect.anchoredPosition, Is.EqualTo(position));
            Assert.That(rect.localScale, Is.EqualTo(scale));
            Assert.That(group.alpha, Is.EqualTo(1f));
            _fixture.Service.CloseUI<UIProbeWindow>(snap: true);
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
        }

        [UnityTest]
        public IEnumerator PresetPlaysCloseThenOpen()
        {
            UIProbeWindow window = _fixture.Open();
            CanvasGroup group = UsePreset(window, 0.1f);
            UniTask close = _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition();
            Assert.That(window.State, Is.EqualTo(UIState.Closing));
            yield return _fixture.Wait(close);
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(group.alpha, Is.Zero);
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            yield return _fixture.Wait(window.AwaitTransition());
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(group.alpha, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator ReopeningMidCloseReversesFromCurrentAlpha()
        {
            UIProbeWindow window = _fixture.Open();
            CanvasGroup group = UsePreset(window, 1f);
            UniTask close = _fixture.Service.CloseUI<UIProbeWindow>().AwaitTransition();
            yield return _fixture.Frames(3);
            float faded = group.alpha;
            Assert.That(faded, Is.LessThan(1f).And.GreaterThan(0f));
            _fixture.Open();
            Assert.That(window.State, Is.EqualTo(UIState.Opening));
            yield return _fixture.Frames(1);
            Assert.That(group.alpha, Is.GreaterThanOrEqualTo(faded));
            yield return _fixture.Wait(window.AwaitTransition());
            yield return _fixture.Wait(close);
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            Assert.That(group.alpha, Is.EqualTo(1f));
        }
    }
}
#endif
