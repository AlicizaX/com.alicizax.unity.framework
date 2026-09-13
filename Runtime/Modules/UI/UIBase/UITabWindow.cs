using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract class UITabWindow<T> : UIWindow<T> where T : UIHolderObjectBase
    {
        private sealed class TabEntry
        {
            internal readonly UIMetadata Definition;
            internal readonly Transform Parent;
            internal UIWidget View;
            internal bool Loading;

            internal TabEntry(UIMetadata definition, Transform parent)
            {
                Definition = definition;
                Parent = parent;
            }
        }

        private sealed class TabRequest
        {
            internal readonly TabEntry Entry;
            internal readonly object[] Arguments;
            internal readonly UniTaskCompletionSource<UIWidget> Completion = new();

            internal TabRequest(TabEntry entry, object[] arguments)
            {
                Entry = entry;
                Arguments = arguments;
            }
        }

        private readonly List<TabEntry> _tabs = new();
        private readonly Dictionary<RuntimeTypeHandle, TabEntry> _tabsByType = new(RuntimeTypeHandleComparer.Instance);
        private UIWidget _activeTab;
        private TabRequest _request;
        private bool _switching;

        protected void InitTabVirtuallyView<TTab>(Transform parent = null) where TTab : UIWidget =>
            RegisterTab(typeof(TTab), parent);

        protected void InitTabVirtuallyView(string typeName, Transform parent = null)
        {
            if (UIMetaRegistry.TryGet(typeName, out var info))
                RegisterTab(Type.GetTypeFromHandle(info.RuntimeTypeHandle), parent);
        }

        private void RegisterTab(Type type, Transform parent)
        {
            if (_tabsByType.ContainsKey(type.TypeHandle)) return;
            if (!typeof(UIWidget).IsAssignableFrom(type))
            {
                Log.Error("[UI] Tab type must be a Widget: {0}", type);
                return;
            }
            UIMetadata metadata = UIMetadata.Create(type);
            if (metadata == null) return;
            var entry = new TabEntry(metadata, parent != null ? parent : baseui.RectTransform);
            _tabs.Add(entry);
            _tabsByType.Add(type.TypeHandle, entry);
        }

        public UniTask<UIWidget> SwitchTab(int index, params object[] userDatas)
        {
            if (DestroyRequested) return UniTask.FromResult<UIWidget>(null);
            if ((uint)index >= (uint)_tabs.Count)
            {
                Log.Error("[UI] Invalid tab index: {0}", index);
                return UniTask.FromResult<UIWidget>(null);
            }
            TabRequest previous = _request;
            var request = new TabRequest(_tabs[index], userDatas);
            _request = request;
            previous?.Completion.TrySetResult(null);
            if (_request != request) return request.Completion.Task;
            TabEntry entry = request.Entry;
            if (entry.View != null) SwitchLoadedTab().Forget();
            else if (!entry.Loading)
            {
                entry.Loading = true;
                LoadTab(entry).Forget();
            }
            return request.Completion.Task;
        }

        private async UniTask LoadTab(TabEntry entry)
        {
            try { entry.View = await CreateWidgetUIAsync(entry.Definition, entry.Parent, false); }
            catch (Exception error) { Log.Exception(error); }
            finally { entry.Loading = false; }
            if (_request?.Entry != entry) return;
            if (entry.View == null || DestroyRequested) CompleteTab(_request, null);
            else SwitchLoadedTab().Forget();
        }

        private async UniTask SwitchLoadedTab()
        {
            if (_switching) return;
            _switching = true;
            try
            {
                while (_request != null && !DestroyRequested)
                {
                    TabRequest request = _request;
                    UIWidget target = request.Entry.View;
                    if (target == null) return;
                    if (_activeTab != null && _activeTab != target)
                    {
                        UIWidget previous = _activeTab;
                        _activeTab = null;
                        previous.Close();
                        await previous.AwaitTransition();
                        continue;
                    }
                    _activeTab = target;
                    target.Open(request.Arguments);
                    CompleteTab(request, target.DestroyRequested ? null : target);
                }
            }
            catch (Exception error)
            {
                Log.Exception(error);
                CompleteTab(_request, null);
            }
            finally { _switching = false; }
        }

        private void CompleteTab(TabRequest request, UIWidget view)
        {
            if (request == null || _request != request) return;
            _request = null;
            request.Completion.TrySetResult(view);
        }

        protected override void OnWidgetRemoved(UIWidget widget)
        {
            if (_activeTab == widget) _activeTab = null;
            if (_tabsByType.TryGetValue(widget.GetType().TypeHandle, out var entry) && entry.View == widget)
                entry.View = null;
            if (DestroyRequested) CompleteTab(_request, null);
        }

        internal override void OnFrameworkDestroyed()
        {
            CompleteTab(_request, null);
            base.OnFrameworkDestroyed();
        }
    }
}
