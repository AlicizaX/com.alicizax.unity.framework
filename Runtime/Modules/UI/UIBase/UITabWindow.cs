using System;
using System.Collections.Generic;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract class UITabWindow<T> : UIBase where T : UIHolderObjectBase
    {
        // 当前激活的Tab页
        private UIWidget _activeTab;

        // 类型顺序索引（根据初始化顺序）
        private readonly List<RuntimeTypeHandle> _typeOrder = new();

        // 页面缓存字典（类型 - 父节点）
        private readonly Dictionary<RuntimeTypeHandle, Transform> _tabCache = new(RuntimeTypeHandleComparer.Instance);

        // 已加载的Tab实例缓存
        private readonly Dictionary<RuntimeTypeHandle, UIWidget> _loadedTabs = new(RuntimeTypeHandleComparer.Instance);

        // 加载状态字典
        private readonly Dictionary<RuntimeTypeHandle, bool> _loadingFlags = new(RuntimeTypeHandleComparer.Instance);
        private sealed class TabSwitchRequest
        {
            public RuntimeTypeHandle TypeHandle;
            public System.Object[] UserDatas;
        }

        private TabSwitchRequest _currentRequest;

        protected T baseui => (T)Holder;

        internal sealed override Type UIHolderType => typeof(T);

        protected void CloseSelf(bool forceClose = false)
        {
            AppServices.App.Require<IUIService>().CloseUI(RuntimeTypeHandler, forceClose);
        }

        internal sealed override void BindUIHolder(UIHolderObjectBase holder, UIBase owner)
        {
            if (_state != UIState.CreatedUI)
            {
                Log.Error("Cannot bind UI holder because tab window has already been created.");
                return;
            }

            BindHolderCommon(holder, owner == null, true);
        }

        // 初始化方法（泛型版本）
        protected void InitTabVirtuallyView<TTab>(Transform parent = null) where TTab : UIWidget
        {
            CacheTabMetadata(typeof(TTab).TypeHandle, parent);
        }

        // 初始化方法（类型名版本）
        protected void InitTabVirtuallyView(string typeName, Transform parent = null)
        {
            if (UIMetaRegistry.TryGet(typeName, out var metaRegistry))
            {
                CacheTabMetadata(metaRegistry.RuntimeTypeHandle, parent);
            }
        }

        private void CacheTabMetadata(RuntimeTypeHandle typeHandle, Transform parent)
        {
            if (!_tabCache.ContainsKey(typeHandle))
            {
                _typeOrder.Add(typeHandle);
                _tabCache[typeHandle] = parent ?? baseui.RectTransform;
            }
        }

        // 无参重载：避免 params 产生空数组分配
        public void SwitchTab(int index)
        {
            SwitchTabInternal(index, null);
        }

        public void SwitchTab(int index, params System.Object[] userDatas)
        {
            SwitchTabInternal(index, userDatas);
        }

        private void SwitchTabInternal(int index, System.Object[] userDatas)
        {
            if (!ValidateIndex(index)) return;

            RuntimeTypeHandle typeHandle = _typeOrder[index];
            var request = new TabSwitchRequest
            {
                TypeHandle = typeHandle,
                UserDatas = userDatas,
            };
            _currentRequest = request;
            if (_loadingFlags.TryGetValue(typeHandle, out var isLoading) && isLoading) return;

            if (_loadedTabs.TryGetValue(typeHandle, out var loadedTab))
            {
                SwitchToLoadedTab(request, loadedTab).Forget();
                return;
            }

            StartAsyncLoading(request).Forget();
        }

        private async UniTask StartAsyncLoading(TabSwitchRequest request)
        {
            RuntimeTypeHandle typeHandle = request.TypeHandle;
            _loadingFlags[typeHandle] = true;
            try
            {
                UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(typeHandle);
                Transform parent = _tabCache[typeHandle];

                UIBase widget = await CreateWidgetUIAsync(metadata, parent, false);
                if (widget is UIWidget tabWidget)
                {
                    _loadedTabs[typeHandle] = tabWidget;
                    if (ReferenceEquals(_currentRequest, request))
                    {
                        SwitchToLoadedTab(request, tabWidget).Forget();
                    }
                }
                else
                {
                    Log.Error("Tab load failed: {0}", Type.GetTypeFromHandle(typeHandle)?.Name);
                }
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
            finally
            {
                _loadingFlags.Remove(typeHandle);
            }
        }

        private async UniTaskVoid SwitchToLoadedTab(TabSwitchRequest request, UIWidget targetTab)
        {
            if (!ReferenceEquals(_currentRequest, request)) return;

            if (_activeTab == targetTab)
            {
                await targetTab.OpenAsync(request.UserDatas);
                return;
            }

            UIWidget previousTab = _activeTab;
            _activeTab = targetTab;
            if (previousTab != null)
            {
                await previousTab.CloseAsync();
            }

            if (!ReferenceEquals(_currentRequest, request) || _activeTab != targetTab)
            {
                return;
            }

            await targetTab.OpenAsync(request.UserDatas);
        }

        protected override void OnWidgetRemoved(UIBase widget)
        {
            if (_activeTab == widget)
            {
                _activeTab = null;
            }

            RuntimeTypeHandle removeKey = default;
            bool found = false;
            foreach (var pair in _loadedTabs)
            {
                if (pair.Value == widget)
                {
                    removeKey = pair.Key;
                    found = true;
                    break;
                }
            }

            if (found)
            {
                _loadedTabs.Remove(removeKey);
            }
        }

        private bool ValidateIndex(int index)
        {
            if (index >= 0 && index < _typeOrder.Count) return true;

            Log.Error("Invalid tab index: {0}", index);
            return false;
        }
    }
}
