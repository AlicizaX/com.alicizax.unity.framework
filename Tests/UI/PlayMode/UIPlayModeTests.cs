#if UNITY_EDITOR
using System.Collections;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.Tests
{
    public sealed class UILifecyclePlayModeTests : UILifecycleCases { }
    public sealed class UINavigationPlayModeTests : UINavigationCases { }
    public sealed class UIMatrixPlayModeTests : UIMatrixCases { }
    public sealed class UIServiceApiPlayModeTests : UIServiceApiCases { }

    public sealed class UIHolderLostPlayModeTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [Test]
        public void LostWidgetHolderDestroysOnlyTheWidget()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            Object.DestroyImmediate(widget.Holder.gameObject);
            Assert.That(widget.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(widget.Destroys, Is.EqualTo(1));
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
        }

        [Test]
        public void LostWindowHolderDestroysWindowAndWidgets()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget widget = window.Create();
            Object.DestroyImmediate(window.Holder.gameObject);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(widget.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(_fixture.Service.GetUI<UIProbeWindow>(), Is.Null);
            Assert.That(_fixture.Open(), Is.Not.SameAs(window));
        }
    }

    public sealed class UITimedPlayModeTests
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [UnityTest]
        public IEnumerator TimedCacheDestroysTheWindowWhenItExpires()
        {
            UITimedCacheWindow window = _fixture.Service.ShowUISync<UITimedCacheWindow>();
            _fixture.Service.CloseUI<UITimedCacheWindow>();
            yield return Elapse(0.5f);
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            Assert.That(_fixture.Service.GetUI<UITimedCacheWindow>(), Is.SameAs(window));
            yield return Elapse(1f);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
            Assert.That(_fixture.Service.GetUI<UITimedCacheWindow>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator ReopeningBeforeExpiryCancelsTheTimer()
        {
            UITimedCacheWindow window = _fixture.Service.ShowUISync<UITimedCacheWindow>();
            _fixture.Service.CloseUI<UITimedCacheWindow>();
            yield return Elapse(0.5f);
            Assert.That(_fixture.Service.ShowUISync<UITimedCacheWindow>(), Is.SameAs(window));
            yield return Elapse(1f);
            Assert.That(window.State, Is.EqualTo(UIState.Opened));
            _fixture.Service.CloseUI<UITimedCacheWindow>();
            yield return Elapse(0.5f);
            Assert.That(window.State, Is.EqualTo(UIState.Closed));
            yield return Elapse(1f);
            Assert.That(window.State, Is.EqualTo(UIState.Destroyed));
        }

        [UnityTest]
        public IEnumerator BlockExpiresAfterItsDuration()
        {
            var info = new UIServiceDebugInfo();
            IUIDebugService debug = _fixture.Service;
            _fixture.Service.SetUIBlock(0.5f);
            yield return Elapse(0.2f);
            debug.FillServiceDebugInfo(info);
            Assert.That(info.BlockActive, Is.True);
            yield return Elapse(0.6f);
            debug.FillServiceDebugInfo(info);
            Assert.That(info.BlockActive, Is.False);
        }

        private IEnumerator Elapse(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            var timer = (IServiceTickable)_fixture.Timer;
            while (Time.unscaledTime < end)
            {
                timer.Tick(Time.unscaledDeltaTime);
                yield return null;
            }
            timer.Tick(Time.unscaledDeltaTime);
        }
    }
}
#endif
