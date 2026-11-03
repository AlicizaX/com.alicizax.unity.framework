#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    internal abstract class UIModelWindow : UIWindow<UIWindowHolder>
    {
        internal static readonly List<UIModelWindow> Created = new();
        internal int Initializes, Opens, Closes, Destroys, Updates, Events, Registrations;
        internal readonly UITickGateTransition Source;
        private Action _eventHandler;

        protected UIModelWindow(bool usesTransition)
        {
            Created.Add(this);
            if (usesTransition) Source = new UITickGateTransition();
        }

        protected override void OnInitialize()
        {
            Initializes++;
            if (Source != null) Holder.SetTransition(Source);
        }

        protected override void OnOpen() => Opens++;
        protected override void OnClose() => Closes++;
        protected override void OnDestroy() => Destroys++;
        protected override void OnUpdate(float deltaTime) => Updates++;

        protected override void OnRegisterEvent(EventListenerProxy proxy)
        {
            Registrations++;
            proxy.AddUIEvent<UIProbeWindowEvent>(_eventHandler ??= () => Events++);
        }
    }

    internal sealed class UIModelCachedWindow : UIModelWindow { public UIModelCachedWindow() : base(false) { } }
    internal sealed class UIModelZeroCacheWindow : UIModelWindow { public UIModelZeroCacheWindow() : base(false) { } }
    internal sealed class UIModelTimedWindow : UIModelWindow { public UIModelTimedWindow() : base(false) { } }
    internal sealed class UIModelTickWindow : UIModelWindow { public UIModelTickWindow() : base(true) { } }
    internal sealed class UIModelTickZeroCacheWindow : UIModelWindow { public UIModelTickZeroCacheWindow() : base(true) { } }

    public abstract class UIWindowModelCases
    {
        private enum ModelState { None, Loading, Open, Closing, Cached }

        private sealed class Slot
        {
            internal Type Type;
            internal RuntimeTypeHandle Handle;
            internal int CacheTime;
            internal bool UsesTransition;
            internal Func<UIWindow> ShowSync;
            internal Action ShowAsync;
            internal Action<bool, bool> Close;
            internal ModelState State;
            internal bool DestroyAfterClose;
            internal LoadEntry Load;
            internal UIModelWindow Instance;
            internal int CreatedCount;
        }

        private sealed class LoadEntry
        {
            internal Slot Slot;
            internal bool Live;
        }

        private UIFixture _fixture;
        private Slot[] _slots;
        private List<Slot> _stack;
        private List<LoadEntry> _pending;
        private HashSet<(Type, ModelState)> _visited;
        private UIService Service => _fixture.Service;

        [SetUp]
        public void SetUp()
        {
            _fixture = new UIFixture();
            UIModelWindow.Created.Clear();
            _stack = new List<Slot>();
            _pending = new List<LoadEntry>();
            _visited = new HashSet<(Type, ModelState)>();
            _slots = new[]
            {
                CreateSlot<UIModelCachedWindow>(-1, false),
                CreateSlot<UIModelZeroCacheWindow>(0, false),
                CreateSlot<UIModelTimedWindow>(1, false),
                CreateSlot<UIModelTickWindow>(-1, true),
                CreateSlot<UIModelTickZeroCacheWindow>(0, true),
            };
        }

        [TearDown] public void TearDown() => _fixture?.Dispose();

        private Slot CreateSlot<T>(int cacheTime, bool usesTransition) where T : UIWindow
        {
            UIMetaRegistry.Register(typeof(T), typeof(UIWindowHolder), UILayer.UI, cacheTime, true);
            UIResRegistry.Register(typeof(UIWindowHolder), "UIProbeWindow", EUIResLoadType.Resources);
            return new Slot
            {
                Type = typeof(T),
                Handle = typeof(T).TypeHandle,
                CacheTime = cacheTime,
                UsesTransition = usesTransition,
                ShowSync = () => Service.ShowUISync<T>(),
                ShowAsync = () => Service.ShowUI<T>().Forget(),
                Close = (force, skip) => Service.CloseUI<T>(force, skip).Forget(),
            };
        }

        [TestCase(11)] [TestCase(23)] [TestCase(47)] [TestCase(89)] [TestCase(131)]
        public void RandomWindowOperationsMatchModel(int seed)
        {
            var random = new System.Random(seed);
            for (int step = 0; step < 2000; step++)
            {
                Slot slot = _slots[random.Next(_slots.Length)];
                int operation = random.Next(11);
                string context = $"seed={seed}, step={step}, op={operation}, type={slot.Type.Name}, state={slot.State}";
                Apply(slot, operation, random, context);
                VerifyModel(context);
            }
            foreach (Slot slot in _slots)
            {
                Assert.That(_visited.Contains((slot.Type, ModelState.Loading)), Is.True, slot.Type.Name + " Loading");
                Assert.That(_visited.Contains((slot.Type, ModelState.Open)), Is.True, slot.Type.Name + " Open");
                if (slot.UsesTransition) Assert.That(_visited.Contains((slot.Type, ModelState.Closing)), Is.True, slot.Type.Name + " Closing");
                if (slot.CacheTime != 0) Assert.That(_visited.Contains((slot.Type, ModelState.Cached)), Is.True, slot.Type.Name + " Cached");
            }
            Settle();
            foreach (UIModelWindow window in UIModelWindow.Created)
            {
                Assert.That(window.State, Is.EqualTo(UIState.Destroyed), window.GetType().Name);
                Assert.That(window.Opens, Is.EqualTo(window.Closes), window.GetType().Name);
                Assert.That(window.Destroys, Is.EqualTo(window.Initializes), window.GetType().Name);
            }
        }

        private void Apply(Slot slot, int operation, System.Random random, string context)
        {
            switch (operation)
            {
                case 0:
                case 1:
                    ShowSync(slot);
                    break;
                case 2:
                case 3:
                    _fixture.Loader.Deferred = random.Next(2) == 0;
                    ShowAsync(slot);
                    break;
                case 4:
                    Close(slot, false, false);
                    break;
                case 5:
                    Close(slot, false, true);
                    break;
                case 6:
                    Close(slot, true, false);
                    break;
                case 7:
                    Deliver();
                    break;
                case 8:
                    FinishTransition(slot);
                    break;
                case 9:
                    TickOnce(context);
                    break;
                case 10:
                    PublishOnce(context);
                    break;
            }
        }

        private void ShowSync(Slot slot)
        {
            if (slot.State == ModelState.Loading) return;
            UIWindow result = slot.ShowSync();
            if (slot.State == ModelState.Closing)
            {
                Assert.That(result, Is.Null);
                return;
            }
            if (slot.State == ModelState.None) MarkCreated(slot);
            slot.State = ModelState.Open;
            MoveToTop(slot);
            Assert.That(result, Is.SameAs(slot.Instance));
        }

        private void ShowAsync(Slot slot)
        {
            ModelState before = slot.State;
            slot.ShowAsync();
            if (before == ModelState.Closing) return;
            if (before == ModelState.None)
            {
                MarkCreated(slot);
                if (_fixture.Loader.Deferred)
                {
                    slot.State = ModelState.Loading;
                    slot.Load = new LoadEntry { Slot = slot, Live = true };
                    _pending.Add(slot.Load);
                }
                else slot.State = ModelState.Open;
            }
            else if (before != ModelState.Loading) slot.State = ModelState.Open;
            MoveToTop(slot);
        }

        private void Close(Slot slot, bool force, bool skip)
        {
            ModelState before = slot.State;
            slot.Close(force, skip);
            switch (before)
            {
                case ModelState.Loading:
                    slot.Load.Live = false;
                    slot.Load = null;
                    Resolve(slot, true);
                    break;
                case ModelState.Open when slot.UsesTransition && !skip:
                    slot.State = ModelState.Closing;
                    slot.DestroyAfterClose = force;
                    break;
                case ModelState.Open:
                    Resolve(slot, force);
                    break;
                case ModelState.Closing:
                    slot.DestroyAfterClose |= force;
                    break;
                case ModelState.Cached when force:
                    slot.State = ModelState.None;
                    slot.Instance = null;
                    break;
            }
        }

        private void Deliver()
        {
            if (_pending.Count == 0) return;
            LoadEntry entry = _pending[0];
            _pending.RemoveAt(0);
            _fixture.Loader.Deliver();
            if (!entry.Live) return;
            entry.Slot.Load = null;
            entry.Slot.State = ModelState.Open;
        }

        private void FinishTransition(Slot slot)
        {
            if (!slot.UsesTransition || slot.Instance == null) return;
            slot.Instance.Source.Finished = true;
            _fixture.Tick();
            if (slot.State != ModelState.Closing) return;
            Resolve(slot, slot.DestroyAfterClose);
        }

        private void TickOnce(string context)
        {
            var expected = new List<(UIModelWindow Window, int Updates)>();
            foreach (Slot slot in _slots)
            {
                if (slot.Instance == null || slot.Instance.State == UIState.CreatedUI) continue;
                int delta = slot.State == ModelState.Open && slot.Instance.State == UIState.Opened ? 1 : 0;
                expected.Add((slot.Instance, slot.Instance.Updates + delta));
            }
            _fixture.Tick();
            foreach ((UIModelWindow window, int updates) in expected)
                Assert.That(window.Updates, Is.EqualTo(updates), context);
        }

        private void PublishOnce(string context)
        {
            var expected = new List<(UIModelWindow Window, int Events)>();
            foreach (Slot slot in _slots)
            {
                if (slot.Instance == null) continue;
                expected.Add((slot.Instance, slot.Instance.Events + (slot.State == ModelState.Open ? 1 : 0)));
            }
            UIFixture.PublishWindow();
            foreach ((UIModelWindow window, int events) in expected)
                Assert.That(window.Events, Is.EqualTo(events), context);
        }

        private void MarkCreated(Slot slot)
        {
            slot.CreatedCount++;
            slot.Instance = FindCreated(slot.Type, slot.CreatedCount);
        }

        private static UIModelWindow FindCreated(Type type, int ordinal)
        {
            int seen = 0;
            foreach (UIModelWindow window in UIModelWindow.Created)
                if (window.GetType() == type && ++seen == ordinal) return window;
            Assert.Fail("Expected instance " + ordinal + " of " + type.Name);
            return null;
        }

        private void MoveToTop(Slot slot)
        {
            _stack.Remove(slot);
            _stack.Add(slot);
        }

        private void Resolve(Slot slot, bool destroy)
        {
            _stack.Remove(slot);
            slot.DestroyAfterClose = false;
            if (destroy || slot.CacheTime == 0)
            {
                slot.State = ModelState.None;
                slot.Instance = null;
            }
            else slot.State = ModelState.Cached;
        }

        private void VerifyModel(string context)
        {
            _fixture.Verify();
            foreach (Slot slot in _slots)
            {
                _visited.Add((slot.Type, slot.State));
                UIWindowRecord record = Service.FindRecord(slot.Handle);
                UIWindow view = record?.View;
                Assert.That(view, Is.SameAs(slot.Instance), context);
                Assert.That(record?.Flight != null, Is.EqualTo(slot.State == ModelState.Loading), context);
                Assert.That(record != null && record.IsCached, Is.EqualTo(slot.State == ModelState.Cached), context);
                if (slot.State == ModelState.Open)
                {
                    Assert.That(view.IsOpen, Is.True, context);
                    Assert.That(view.State, Is.EqualTo(UIState.Opening).Or.EqualTo(UIState.Opened), context);
                }
                else if (view != null)
                {
                    Assert.That(view.IsOpen, Is.False, context);
                    if (slot.State == ModelState.Closing) Assert.That(view.State, Is.EqualTo(UIState.Closing), context);
                    if (slot.State == ModelState.Cached) Assert.That(view.State, Is.EqualTo(UIState.Closed), context);
                }
            }
            int index = 0;
            UILayerStack stack = Service.LayerStack((int)UILayer.UI);
            for (UIWindowRecord record = stack.Bottom; record != null; record = record.StackAbove, index++)
            {
                Assert.That(index, Is.LessThan(_stack.Count), context);
                Assert.That(record.View, Is.SameAs(_stack[index].Instance), context);
            }
            Assert.That(index, Is.EqualTo(_stack.Count), context);
            foreach (UIModelWindow window in UIModelWindow.Created)
            {
                Assert.That(window.Initializes, Is.LessThanOrEqualTo(1), context);
                Assert.That(window.Destroys, Is.LessThanOrEqualTo(1), context);
                Assert.That(window.Registrations, Is.LessThanOrEqualTo(1), context);
                Assert.That(window.Opens - window.Closes, Is.InRange(0, 1), context);
            }
        }

        private void Settle()
        {
            foreach (Slot slot in _slots)
            {
                if (slot.State == ModelState.None) continue;
                Close(slot, true, false);
                FinishTransition(slot);
            }
            while (_pending.Count > 0) Deliver();
            VerifyModel("settle");
        }
    }

    public abstract class UILeakCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture?.Dispose();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference[] DestroyWindowTree()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget first = window.Create();
            UIProbeWidget second = first.Create();
            UIProbeWidget third = window.Create();
            var references = new[]
            {
                new WeakReference(window), new WeakReference(window.Holder),
                new WeakReference(first), new WeakReference(first.Holder),
                new WeakReference(second), new WeakReference(second.Holder),
                new WeakReference(third), new WeakReference(third.Holder),
            };
            _fixture.Service.CloseUI<UIProbeWindow>(force: true).GetAwaiter().GetResult();
            return references;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private WeakReference[] DestroyCachedWindowThroughTransition()
        {
            UIProbeWindow window = _fixture.Open();
            var transition = new UIGateTransition();
            window.Holder.SetTransition(transition);
            UniTask closing = _fixture.Service.CloseUI<UIProbeWindow>(force: true);
            var references = new[] { new WeakReference(window), new WeakReference(window.Holder), new WeakReference(transition) };
            transition.Finish();
            closing.GetAwaiter().GetResult();
            return references;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private (UIProbeWidget Retained, WeakReference View, WeakReference Holder) DestroyWidgetBehindRetainedSibling()
        {
            UIProbeWindow window = _fixture.Open();
            UIProbeWidget retained = window.Create();
            UIProbeWidget next = window.Create();
            retained.DestroyNow();
            var view = new WeakReference(next);
            var holder = new WeakReference(next.Holder);
            next.DestroyNow();
            return (retained, view, holder);
        }

        private static void CollectGarbage()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private static IEnumerator WaitForDestruction()
        {
            yield return null;
            yield return null;
            CollectGarbage();
        }

        [UnityTest]
        public IEnumerator DestroyedWindowTreeIsCollectable()
        {
            WeakReference[] references = DestroyWindowTree();
            yield return WaitForDestruction();
            for (int i = 0; i < references.Length; i++)
                Assert.That(references[i].IsAlive, Is.False, "reference " + i);
        }

        [UnityTest]
        public IEnumerator DestroyAfterTransitionIsCollectable()
        {
            WeakReference[] references = DestroyCachedWindowThroughTransition();
            yield return WaitForDestruction();
            for (int i = 0; i < references.Length; i++)
                Assert.That(references[i].IsAlive, Is.False, "reference " + i);
        }

        [UnityTest]
        public IEnumerator RetainedDestroyedWidgetKeepsSiblingViewButNotItsHolder()
        {
            (UIProbeWidget retained, WeakReference view, WeakReference holder) = DestroyWidgetBehindRetainedSibling();
            yield return WaitForDestruction();
            Assert.That(holder.IsAlive, Is.False);
            Assert.That(retained.NextSibling, Is.SameAs(view.Target));
            GC.KeepAlive(retained);
        }
    }
}
#endif
