using System;
using System.Buffers;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract partial class UIBase
    {
        private List<UIWidget> _children;

        private UIWidget[] SnapshotChildren(out int count)
        {
            count = _children?.Count ?? 0;
            if (count == 0) return null;
            UIWidget[] snapshot = ArrayPool<UIWidget>.Shared.Rent(count);
            _children.CopyTo(snapshot);
            return snapshot;
        }

        private static void ReturnChildren(UIWidget[] snapshot)
        {
            if (snapshot != null) ArrayPool<UIWidget>.Shared.Return(snapshot, true);
        }

        private UniTask OpenChildren(int generation)
        {
            UIWidget[] snapshot = SnapshotChildren(out int count);
            List<UniTask> pending = null;
            try
            {
                for (int i = 0; i < count && IsCurrent(generation) && ChildrenCanOpen; i++)
                {
                    UIWidget child = snapshot[i];
                    if (!child.OpenIntent || child.DestroyRequested) continue;
                    (pending ??= new List<UniTask>()).Add(OpenChild(child));
                }
            }
            finally { ReturnChildren(snapshot); }
            return pending == null ? UniTask.CompletedTask : UniTask.WhenAll(pending);
        }

        private UniTask CloseChildren(bool destroy, bool skipTransition)
        {
            int generation = _transitionGeneration;
            UIWidget[] snapshot = SnapshotChildren(out int count);
            List<UniTask> pending = null;
            try
            {
                for (int i = 0; i < count && IsCurrent(generation); i++)
                    (pending ??= new List<UniTask>()).Add(CloseChild(snapshot[i], destroy, skipTransition));
            }
            finally { ReturnChildren(snapshot); }
            return pending == null ? UniTask.CompletedTask : UniTask.WhenAll(pending);
        }

        private static async UniTask OpenChild(UIWidget child)
        {
            try
            {
                child.OpenFromParent();
                await child.AwaitTransition();
            }
            catch (Exception error) { Log.Exception(error); }
        }

        private static async UniTask CloseChild(UIWidget child, bool destroy, bool skipTransition)
        {
            try { await child.CloseFromParent(destroy, skipTransition); }
            catch (Exception error) { Log.Exception(error); }
        }

        private void DestroyChildrenImmediate()
        {
            UIWidget[] snapshot = SnapshotChildren(out int count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    try { snapshot[i].DestroyNow(); }
                    catch (Exception error) { Log.Exception(error); }
                }
            }
            finally { ReturnChildren(snapshot); }
        }

        internal async UniTask<UIWidget> CreateWidgetUIAsync(UIMetadata metadata, Transform parent, bool visible)
        {
            UIWidget widget = BeginWidgetCreate(metadata, visible);
            if (widget == null) return null;
            var cancellation = widget.BeginResourceLoad();
            bool loaded = await UIHolderFactory.CreateUIResourceAsync(widget, parent, cancellation.Token);
            widget.EndResourceLoad(cancellation);
            if (!loaded)
            {
                widget.DestroyNow();
                return null;
            }
            return FinishWidgetCreate(widget);
        }

        internal UIWidget CreateWidgetUISync(UIMetadata metadata, Transform parent, bool visible)
        {
            UIWidget widget = BeginWidgetCreate(metadata, visible);
            if (widget == null) return null;
            if (!UIHolderFactory.CreateUIResourceSync(widget, parent))
            {
                widget.DestroyNow();
                return null;
            }
            return FinishWidgetCreate(widget);
        }

        protected async UniTask<UIWidget> CreateWidgetAsync(string typeName, Transform parent, bool visible = true)
        {
            if (!UIMetaRegistry.TryGet(typeName, out var info)) return null;
            return await CreateWidgetUIAsync(UIMetadata.Create(Type.GetTypeFromHandle(info.RuntimeTypeHandle)), parent, visible);
        }

        protected async UniTask<T> CreateWidgetAsync<T>(Transform parent, bool visible = true) where T : UIWidget =>
            (T)await CreateWidgetUIAsync(UIMetadata.Create(typeof(T)), parent, visible);

        protected UIWidget CreateWidgetSync(string typeName, Transform parent, bool visible = true)
        {
            if (!UIMetaRegistry.TryGet(typeName, out var info)) return null;
            return CreateWidgetUISync(UIMetadata.Create(Type.GetTypeFromHandle(info.RuntimeTypeHandle)), parent, visible);
        }

        protected T CreateWidgetSync<T>(Transform parent, bool visible = true) where T : UIWidget =>
            (T)CreateWidgetUISync(UIMetadata.Create(typeof(T)), parent, visible);

        protected T CreateWidgetSync<T>(UIHolderObjectBase holder, bool destroyHolderOnDispose = false) where T : UIWidget
        {
            if (holder == null || !holder.IsValid()) return null;
            UIMetadata metadata = UIMetadata.Create(typeof(T));
            if (metadata == null) return null;
            if (!Type.GetTypeFromHandle(metadata.MetaInfo.HolderRuntimeTypeHandle).IsInstanceOfType(holder))
            {
                Log.Exception(new ArgumentException("Holder type does not match the widget.", nameof(holder)));
                return null;
            }
            UIWidget widget = BeginWidgetCreate(metadata, true);
            if (widget == null) return null;
            widget.SetDestroyHolderOnDispose(destroyHolderOnDispose);
            widget.BindUIHolder(holder);
            return (T)FinishWidgetCreate(widget);
        }

        private UIWidget BeginWidgetCreate(UIMetadata metadata, bool visible)
        {
            if (DestroyRequested || metadata == null) return null;
            if (!typeof(UIWidget).IsAssignableFrom(metadata.UILogicType))
            {
                Log.Error("[UI] The UI type must be a Widget.");
                return null;
            }
            UIWidget widget = (UIWidget)metadata.CreateUI(Service);
            if (widget == null) return null;
            widget.Parent = this;
            widget.OpenIntent = visible;
            (_children ??= new List<UIWidget>(4)).Add(widget);
            return widget;
        }

        private UIWidget FinishWidgetCreate(UIWidget widget)
        {
            if (DestroyRequested || widget.DestroyRequested) return null;
            bool initialized = widget.InternalInitialize();
            if (initialized && widget.OpenIntent && ChildrenCanOpen) widget.InternalOpen();
            return widget.DestroyRequested ? null : widget;
        }

        public UICloseHandle RemoveWidget(UIWidget widget) =>
            widget != null && widget.Parent == this ? widget.Destroy() : default;

        internal void DetachWidget(UIWidget widget)
        {
            _children.Remove(widget);
            try { OnWidgetRemoved(widget); }
            catch (Exception error) { Log.Exception(error); }
        }

        protected virtual void OnWidgetRemoved(UIWidget widget) { }
    }
}
