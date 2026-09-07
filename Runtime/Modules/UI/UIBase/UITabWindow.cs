using System;
using System.Collections.Generic;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlicizaX.UI.Runtime
{
    public abstract class UITabWindow<T> : UIWindow<T> where T : UIHolderObjectBase
    {
        private UIWidget _activeTab;
        private readonly List<RuntimeTypeHandle> _typeOrder = new();
        private readonly Dictionary<RuntimeTypeHandle, Transform> _tabParents = new(RuntimeTypeHandleComparer.Instance);
        private readonly Dictionary<RuntimeTypeHandle, VisualElement> _toolkitTabParents = new(RuntimeTypeHandleComparer.Instance);
        private readonly Dictionary<RuntimeTypeHandle, UIWidget> _loadedTabs = new(RuntimeTypeHandleComparer.Instance);
        private readonly HashSet<RuntimeTypeHandle> _loadingTabs = new(RuntimeTypeHandleComparer.Instance);

        private int _requestVersion;
        private RuntimeTypeHandle _requestTypeHandle;
        private System.Object[] _requestUserDatas;

        protected void InitTabVirtuallyView<TTab>(Transform parent = null) where TTab : UIWidget
        {
            CacheTabMetadata(typeof(TTab).TypeHandle, parent);
        }

        protected void InitTabVirtuallyView(string typeName, Transform parent = null)
        {
            if (UIMetaRegistry.TryGet(typeName, out var metaRegistry))
            {
                CacheTabMetadata(metaRegistry.RuntimeTypeHandle, parent);
            }
        }

        protected void InitTabVirtuallyView<TTab>(VisualElement parent) where TTab : UIWidget
        {
            CacheTabMetadata(typeof(TTab).TypeHandle, parent);
        }

        protected void InitTabVirtuallyView(string typeName, VisualElement parent)
        {
            if (UIMetaRegistry.TryGet(typeName, out var metaRegistry))
            {
                CacheTabMetadata(metaRegistry.RuntimeTypeHandle, parent);
            }
        }

        private void CacheTabMetadata(RuntimeTypeHandle typeHandle, Transform parent)
        {
            if (_tabParents.ContainsKey(typeHandle) || _toolkitTabParents.ContainsKey(typeHandle))
            {
                return;
            }

            _typeOrder.Add(typeHandle);
            if (parent != null || baseui is not UIToolkitHolderBase toolkitHolder)
            {
                _tabParents[typeHandle] = parent ?? baseui.RectTransform;
            }
            else
            {
                _toolkitTabParents[typeHandle] = toolkitHolder.RootVisualElement;
            }
        }

        private void CacheTabMetadata(RuntimeTypeHandle typeHandle, VisualElement parent)
        {
            if (_tabParents.ContainsKey(typeHandle) || _toolkitTabParents.ContainsKey(typeHandle))
            {
                return;
            }

            _typeOrder.Add(typeHandle);
            _toolkitTabParents[typeHandle] = parent;
        }

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
            if (index < 0 || index >= _typeOrder.Count)
            {
                Log.Error("Invalid tab index: {0}", index);
                return;
            }

            RuntimeTypeHandle typeHandle = _typeOrder[index];
            _requestTypeHandle = typeHandle;
            _requestUserDatas = userDatas;
            int version = ++_requestVersion;

            if (_loadingTabs.Contains(typeHandle))
            {
                return;
            }

            if (_loadedTabs.TryGetValue(typeHandle, out var loadedTab))
            {
                SwitchToLoadedTab(version, loadedTab).Forget();
                return;
            }

            StartAsyncLoading(typeHandle).Forget();
        }

        private async UniTaskVoid StartAsyncLoading(RuntimeTypeHandle typeHandle)
        {
            _loadingTabs.Add(typeHandle);
            try
            {
                UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(typeHandle);
                UIBase widget = _toolkitTabParents.TryGetValue(typeHandle, out VisualElement toolkitParent)
                    ? await CreateWidgetUIAsync(metadata, toolkitParent, false)
                    : await CreateWidgetUIAsync(metadata, _tabParents[typeHandle], false);
                if (widget is not UIWidget tabWidget)
                {
                    Log.Error("Tab load failed: {0}", Type.GetTypeFromHandle(typeHandle)?.Name);
                    return;
                }

                _loadedTabs[typeHandle] = tabWidget;
                if (typeHandle.Value == _requestTypeHandle.Value)
                {
                    SwitchToLoadedTab(_requestVersion, tabWidget).Forget();
                }
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
            finally
            {
                _loadingTabs.Remove(typeHandle);
            }
        }

        private async UniTaskVoid SwitchToLoadedTab(int version, UIWidget targetTab)
        {
            if (!IsCurrentRequest(version))
            {
                return;
            }

            System.Object[] userDatas = _requestUserDatas;
            if (_activeTab == targetTab)
            {
                await targetTab.OpenAsync(userDatas);
                return;
            }

            UIWidget previousTab = _activeTab;
            _activeTab = targetTab;
            if (previousTab != null)
            {
                await previousTab.CloseAsync();
            }

            if (!IsCurrentRequest(version) || _activeTab != targetTab)
            {
                return;
            }

            await targetTab.OpenAsync(_requestUserDatas);
        }

        private bool IsCurrentRequest(int version)
        {
            return version == _requestVersion;
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
                if (pair.Value != widget)
                {
                    continue;
                }

                removeKey = pair.Key;
                found = true;
                break;
            }

            if (!found)
            {
                return;
            }

            _loadedTabs.Remove(removeKey);
            _loadingTabs.Remove(removeKey);
        }
    }
}
