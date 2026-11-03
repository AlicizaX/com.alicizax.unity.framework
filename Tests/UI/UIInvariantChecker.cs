#if UNITY_EDITOR
using System.Collections.Generic;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace AlicizaX.UI.Tests
{
    internal static class UIInvariantChecker
    {
        internal static void Verify(UIService service)
        {
            var views = new List<UIBase>();
            foreach (UIWindowRecord record in service.Records) VerifyRecord(record, views);
            VerifyLayerStacks(service);
            VerifyViews(views);
            VerifyUpdateList(service, views);
            VerifyTransitionList(service, views);
        }

        private static void VerifyRecord(UIWindowRecord record, List<UIBase> views)
        {
            string name = record.Metadata.UILogicTypeName;
            UIWindow view = record.View;
            if (view != null)
            {
                Assert.That(view.State, Is.Not.EqualTo(UIState.Destroying).And.Not.EqualTo(UIState.Destroyed), "I1 " + name);
                Collect(view, views);
            }
            if (record.Flight != null)
            {
                Assert.That(view, Is.Not.Null, "I2 " + name);
                Assert.That(view.State, Is.EqualTo(UIState.CreatedUI).Or.EqualTo(UIState.Loaded), "I2 " + name);
            }
            Assert.That(record.IsStacked && record.IsCached, Is.False, "I3 " + name);
            if (!record.IsCached) return;
            Assert.That(view.State, Is.EqualTo(UIState.Closed), "I12 " + name);
            Assert.That(view.IsOpen, Is.False, "I12 " + name);
            Assert.That(view.HasPendingOpen, Is.False, "I12 " + name);
        }

        private static void Collect(UIBase view, List<UIBase> views)
        {
            views.Add(view);
            for (UIWidget child = view.FirstChild; child != null; child = child.NextSibling) Collect(child, views);
        }

        private static void VerifyLayerStacks(UIService service)
        {
            for (int layer = 0; layer < (int)UILayer.All; layer++)
            {
                int previous = -1, count = 0;
                UILayerStack stack = service.LayerStack(layer);
                for (UIWindowRecord record = stack.Bottom; record != null; record = record.StackAbove)
                {
                    Assert.That(record.Order, Is.GreaterThan(previous), "I3 order");
                    Assert.That(record.View.Depth, Is.EqualTo(layer * UIService.LAYER_DEEP + record.Order * UIService.WINDOW_DEEP), "I3 depth");
                    previous = record.Order;
                    count++;
                }
                Assert.That(stack.Count, Is.EqualTo(count), "I3 count");
            }
        }

        private static void VerifyViews(List<UIBase> views)
        {
            foreach (UIBase view in views)
            {
                string name = view.GetType().Name;
                Assert.That(view.Effective, Is.EqualTo(view.IsOpen && !view.DestroyRequested && view.ParentEffective), "I4 " + name);
                if (view.Holder != null)
                {
                    Assert.That(view.IsTransitionPending, Is.EqualTo(view.State is UIState.Opening or UIState.Closing), "I8 " + name);
                }
                if (!view.Effective && view.EventListeners != null)
                    Assert.That(view.EventListeners.IsSuspended, Is.True, "I9 " + name);
            }
        }

        private static void VerifyUpdateList(UIService service, List<UIBase> views)
        {
            var linked = new Dictionary<UIBase, int>();
            int index = 0;
            for (UITickNode node = service.UpdateViews.First; node != null; node = node.Next) linked.Add(node.View, index++);
            Assert.That(service.UpdateViews.Count, Is.EqualTo(linked.Count), "I5 count");
            int expected = 0;
            foreach (UIBase view in views)
            {
                if (view.IsUpdating)
                {
                    expected++;
                    Assert.That(linked.ContainsKey(view), Is.True, "I5 " + view.GetType().Name);
                    if (view is UIWidget widget)
                    {
                        UIBase ancestor = widget.Parent;
                        while (ancestor is UIWidget parent && !parent.IsUpdating) ancestor = parent.Parent;
                        if (ancestor.IsUpdating)
                            Assert.That(linked[ancestor], Is.LessThan(linked[view]), "I11 " + view.GetType().Name);
                    }
                }
            }
            Assert.That(linked.Count, Is.EqualTo(expected), "I5 membership");
        }

        private static void VerifyTransitionList(UIService service, List<UIBase> views)
        {
            int expected = 0;
            foreach (UIBase view in views)
                if (view.Holder != null && view.Holder.HasActiveTickTransition) expected++;
            Assert.That(service.TransitionViews.Count, Is.EqualTo(expected), "I6");
        }
    }
}
#endif
