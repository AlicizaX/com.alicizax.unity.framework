using System;
using System.Collections.Generic;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    public abstract class UITabWindow<T> : UIWindowBase<T> where T : UIHolderObjectBase
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
        private struct TabSwitchRequest
        {
            public RuntimeTypeHandle TypeHandle;
            public System.Object[] UserDatas;
            public int Version;
        }

        private int _currentRequestVersion;
        private RuntimeTypeHandle _currentRequestTypeHandle;
        private System.Object[] _currentRequestUserDatas;

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
            SetCurrentRequest(typeHandle, userDatas);
            if (_loadingFlags.TryGetValue(typeHandle, out var isLoading) && isLoading) return;

            if (_loadedTabs.TryGetValue(typeHandle, out var loadedTab))
            {
                SwitchToLoadedTab(GetCurrentRequest(), loadedTab).Forget();
                return;
            }

            StartAsyncLoading(GetCurrentRequest()).Forget();
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
                    if (IsCurrentRequestType(typeHandle))
                    {
                        SwitchToLoadedTab(GetCurrentRequest(), tabWidget).Forget();
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
            if (!IsCurrentRequest(request)) return;

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

            if (!IsCurrentRequest(request) || _activeTab != targetTab)
            {
                return;
            }

            await targetTab.OpenAsync(request.UserDatas);
        }

        private bool IsCurrentRequest(TabSwitchRequest request)
        {
            return request.Version == _currentRequestVersion;
        }

        private bool IsCurrentRequestType(RuntimeTypeHandle typeHandle)
        {
            return typeHandle.Value == _currentRequestTypeHandle.Value;
        }

        private void SetCurrentRequest(RuntimeTypeHandle typeHandle, System.Object[] userDatas)
        {
            _currentRequestTypeHandle = typeHandle;
            _currentRequestUserDatas = userDatas;
            _currentRequestVersion++;
        }

        private TabSwitchRequest GetCurrentRequest()
        {
            return new TabSwitchRequest
            {
                TypeHandle = _currentRequestTypeHandle,
                UserDatas = _currentRequestUserDatas,
                Version = _currentRequestVersion,
            };
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
