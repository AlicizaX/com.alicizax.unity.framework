using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.UI.Tests
{
    public abstract class UIWidgetCases
    {
        private UIFixture _fixture;
        [SetUp] public void SetUp() => _fixture = new UIFixture();
        [TearDown] public void TearDown() => _fixture.Dispose();

        [TestCase(0, false)] [TestCase(0, true)]
        [TestCase(1, false)] [TestCase(1, true)]
        [TestCase(8, false)] [TestCase(8, true)]
        public void CreateUnderHiddenAncestorKeepsIndependentState(int depth, bool open)
        {
            var window = _fixture.Open();
            var parent = window.Create();
            for (int i = 0; i < depth; i++) parent = parent.Create();
            parent.Close();
            var child = parent.Create(open);
            var grandchild = child.Create();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.IsOpen, Is.EqualTo(open));
            Assert.That(grandchild.IsOpen, Is.True);
            Assert.That(grandchild.Events + grandchild.Updates, Is.Zero);
            parent.Open();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.IsVisible, Is.EqualTo(open));
            Assert.That(grandchild.Events, Is.EqualTo(open ? 1 : 0));
            Assert.That(grandchild.Updates, Is.EqualTo(open ? 1 : 0));
            Assert.That(grandchild.Opens, Is.EqualTo(1));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void DestroyWidgetInEveryStateReleasesSubtreeOnce(int state)
        {
            var child = _fixture.Open().Create(false);
            var descendant = child.Create();
            var gate = new UIGateTransition();
            if (state == 1 || state == 3) child.Holder.SetTransition(gate);
            if (state != 0) child.Open();
            if (state == 3) { gate.Finish(); child.Close(); }
            if (state == 4) child.Close();
            child.DestroyNow();
            child.DestroyNow();
            if (gate.Pending != null) gate.Finish();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(child.Closes, Is.EqualTo(state == 0 ? 0 : 1));
            Assert.That(descendant.Closes, Is.EqualTo(1));
            Assert.That(descendant.Destroys, Is.EqualTo(1));
            Assert.That(child.Holder, Is.Null);
            Assert.That(descendant.Events + descendant.Updates, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void SameTypeInstancesHaveIndependentOwnership(bool removeThroughParent)
        {
            var window = _fixture.Open();
            var a = window.Create();
            var b = window.Create();
            var leaf = a.Create();
            Assert.That(a, Is.Not.SameAs(b));
            Assert.That(a.Holder, Is.Not.SameAs(b.Holder));
            if (removeThroughParent) window.RemoveWidget(a).GetAwaiter().GetResult();
            else a.Destroy().GetAwaiter().GetResult();
            UIFixture.Publish();
            _fixture.Tick();
            Assert.That(a.Destroys + leaf.Destroys, Is.EqualTo(2));
            Assert.That(b.Events, Is.EqualTo(1));
            Assert.That(b.Updates, Is.EqualTo(1));
            Assert.That(b.Destroys, Is.Zero);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void UpdateMayRemoveSelfSiblingOrParentWithoutSkippingOtherBranch(int target)
        {
            var window = _fixture.Open();
            var parent = window.Create();
            var a = parent.Create();
            var b = parent.Create();
            var outside = window.Create();
            a.Updating = () => (target == 0 ? a : target == 1 ? b : parent).DestroyNow();
            _fixture.Tick();
            Assert.That(a.Updates, Is.EqualTo(1));
            Assert.That(b.Updates, Is.EqualTo(target == 0 ? 1 : 0));
            Assert.That(outside.Updates, Is.EqualTo(1));
        }

        [Test]
        public void AddedSiblingDuringUpdateStartsOnNextTraversal()
        {
            var window = _fixture.Open();
            var a = window.Create();
            var b = window.Create();
            UIProbeWidget added = null;
            a.Updating = () => { a.Updating = null; added = window.Create(); };
            _fixture.Tick();
            Assert.That(added.IsOpen, Is.True);
            Assert.That(added.Updates, Is.Zero);
            Assert.That(b.Updates, Is.EqualTo(1));
            _fixture.Tick();
            Assert.That(added.Updates, Is.EqualTo(1));
        }

        [Test]
        public void RemoveForeignOrNullWidgetDoesNotChangeEitherTree()
        {
            var window = _fixture.Open();
            var a = window.Create();
            var b = window.Create();
            var child = b.Create();
            a.RemoveWidget(child).GetAwaiter().GetResult();
            a.RemoveWidget(null).GetAwaiter().GetResult();
            _fixture.Tick();
            Assert.That(child.Updates, Is.EqualTo(1));
            Assert.That(child.Destroys, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void BorrowedHolderHonorsDestructionOption(bool destroyHolder)
        {
            var window = _fixture.Open();
            var obj = new GameObject("borrowed", typeof(RectTransform), typeof(UIWidgetHolder));
            obj.transform.SetParent(window.Holder.transform, false);
            var holder = obj.GetComponent<UIWidgetHolder>();
            var child = window.Borrow(holder, destroyHolder);
            child.DestroyNow();
            Assert.That(child.Destroys, Is.EqualTo(1));
            Assert.That(child.Holder, Is.Null);
            if (!destroyHolder)
            {
                Assert.That(holder == null, Is.False);
                Assert.That(holder.GetComponent<CanvasGroup>().alpha, Is.Zero);
                var replacement = window.Borrow(holder, false);
                Assert.That(replacement.IsOpen, Is.True);
                Assert.That(replacement, Is.Not.SameAs(child));
            }
            else if (!Application.isPlaying) Assert.That(holder == null, Is.True);
        }

        [Test]
        public void BorrowingOutsideParentSubtreeReturnsFailure()
        {
            var window = _fixture.Open();
            var obj = new GameObject("foreign", typeof(RectTransform), typeof(UIWidgetHolder));
            obj.transform.SetParent(_fixture.Root.transform, false);
            LogAssert.Expect(LogType.Error, new Regex("Transform subtree"));
            Assert.That(window.Borrow(obj.GetComponent<UIWidgetHolder>()), Is.Null);
            Assert.That(obj == null, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void CreationAfterDestroyRequestFailsWithoutLoading(bool duringClose)
        {
            var window = _fixture.Open();
            var child = window.Create();
            int loads = _fixture.Loader.Loads;
            void Create()
            {
                LogAssert.Expect(LogType.Error, new Regex("parent requested destruction"));
                Assert.That(child.Create(), Is.Null);
            }
            if (duringClose) child.Closing = Create;
            else child.Destroying = Create;
            child.Destroy().GetAwaiter().GetResult();
            Assert.That(_fixture.Loader.Loads, Is.EqualTo(loads));
        }

        [Test]
        public void DestroyCallbacksCanRemoveDescendantsBeforeCascade()
        {
            var root = _fixture.Open().Create();
            var a = root.Create();
            var b = root.Create();
            var leaf = a.Create();
            root.Destroying = () => a.DestroyNow();
            a.Destroying = () => b.DestroyNow();
            root.DestroyNow();
            Assert.That(root.Destroys + a.Destroys + b.Destroys + leaf.Destroys, Is.EqualTo(4));
            Assert.That(root.Closes + a.Closes + b.Closes + leaf.Closes, Is.EqualTo(4));
        }

        [Test]
        public void ClosingDuringEventDispatchStopsFutureDispatchesWithoutDuplicateResume()
        {
            var window = _fixture.Open();
            var first = window.Create();
            var second = window.Create();
            first.Receiving = () => _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            UIFixture.Publish();
            int previous = second.Events;
            UIFixture.Publish();
            Assert.That(first.Events, Is.EqualTo(1));
            Assert.That(second.Events, Is.EqualTo(previous));
            first.Receiving = null;
            for (int i = 0; i < 20; i++)
            {
                _fixture.Open();
                UIFixture.Publish();
                _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
            }
            Assert.That(first.Events, Is.EqualTo(21));
            Assert.That(second.Events, Is.EqualTo(previous + 20));
            Assert.That(first.Opens + second.Opens, Is.EqualTo(2));
        }

        [TestCase(7)] [TestCase(31)] [TestCase(97)]
        public void MixedTreeOperationsMatchIndependentLogicalModel(int seed)
        {
            var random = new System.Random(seed);
            var window = _fixture.Open();
            var nodes = new List<UIProbeWidget>();
            var parents = new List<int>();
            var open = new List<bool>();
            var alive = new List<bool>();
            bool windowOpen = true;
            for (int step = 0; step < 100; step++)
            {
                int index = nodes.Count == 0 ? -1 : random.Next(nodes.Count);
                int op = random.Next(5);
                if (index < 0 || op == 0)
                {
                    int parent = index >= 0 && alive[index] ? index : -1;
                    bool initial = random.Next(2) == 0;
                    nodes.Add(parent < 0 ? window.Create(initial) : nodes[parent].Create(initial));
                    parents.Add(parent); open.Add(initial); alive.Add(true);
                }
                else if (op == 4)
                {
                    if (windowOpen) _fixture.Service.CloseUI<UIProbeWindow>().GetAwaiter().GetResult();
                    else _fixture.Open();
                    windowOpen = !windowOpen;
                }
                else if (alive[index])
                {
                    if (op == 1) { nodes[index].Open(); open[index] = true; }
                    else if (op == 2) { nodes[index].Close(); open[index] = false; }
                    else
                    {
                        nodes[index].DestroyNow();
                        for (int n = index; n < nodes.Count; n++)
                            for (int p = n; p >= 0; p = parents[p])
                                if (p == index) { alive[n] = false; open[n] = false; break; }
                    }
                }
                var beforeUpdates = nodes.ConvertAll(n => n.Updates);
                var beforeEvents = nodes.ConvertAll(n => n.Events);
                UIFixture.Publish();
                _fixture.Tick();
                for (int n = 0; n < nodes.Count; n++)
                {
                    bool effective = windowOpen && alive[n] && open[n];
                    for (int p = parents[n]; p >= 0; p = parents[p]) effective &= alive[p] && open[p];
                    Assert.That(nodes[n].IsOpen, Is.EqualTo(alive[n] && open[n]), $"seed={seed}, step={step}, node={n}");
                    Assert.That(nodes[n].Updates - beforeUpdates[n], Is.EqualTo(effective ? 1 : 0));
                    Assert.That(nodes[n].Events - beforeEvents[n], Is.EqualTo(effective ? 1 : 0));
                }
            }
        }
    }
}
