using System;
using System.Collections.Generic;
using AlicizaX;
using Cysharp.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.Runtime
{
    public abstract class UITabWindow<T> : UIBase where T : UIHolderObjectBase
    {
        // 当前激活的Tab页
        private UIWidget _activeTab;

        // 类型顺序索引（根据初始化顺序）
        private readonly List<RuntimeTypeHandle> _typeOrder = new();

        // 页面缓存字典（类型 - 父节点）
        private readonly Dictionary<RuntimeTypeHandle, Transform> _tabCache = new();

        // 已加载的Tab实例缓存
        private readonly Dictionary<RuntimeTypeHandle, UIWidget> _loadedTabs = new();

        // 加载状态字典
        private readonly Dictionary<RuntimeTypeHandle, bool> _loadingFlags = new();

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

            Holder = holder;
            _canvas = Holder.transform.GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.overrideSorting = owner == null;
            }
            _raycaster = Holder.transform.GetComponent<GraphicRaycaster>();
            Holder.RectTransform.localPosition = Vector3.zero;
            Holder.RectTransform.pivot = new Vector2(0.5f, 0.5f);
            Holder.RectTransform.anchorMin = Vector2.zero;
            Holder.RectTransform.anchorMax = Vector2.one;
            Holder.RectTransform.offsetMin = Vector2.zero;
            Holder.RectTransform.offsetMax = Vector2.zero;
            Holder.RectTransform.localScale = Vector3.one;
            SetState(UIState.Loaded);
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

        public void SwitchTab(int index, params System.Object[] userDatas)
        {
            if (!ValidateIndex(index)) return;

            var typeHandle = _typeOrder[index];
            if (_loadingFlags.TryGetValue(typeHandle, out var isLoading) && isLoading) return;

            if (_loadedTabs.TryGetValue(typeHandle, out var loadedTab))
            {
                SwitchToLoadedTab(loadedTab, userDatas);
                return;
            }

            StartAsyncLoading(typeHandle, userDatas).Forget();
        }

        private async UniTask StartAsyncLoading(RuntimeTypeHandle typeHandle, params System.Object[] userDatas)
        {
            _loadingFlags[typeHandle] = true;
            UIMetadata metadata = UIMetadataFactory.GetWidgetMetadata(typeHandle);
            Transform parent = _tabCache[typeHandle];

            UIBase widget = await CreateWidgetUIAsync(metadata, parent, false);
            _loadingFlags.Remove(typeHandle);
            if (widget is UIWidget tabWidget)
            {
                _loadedTabs[typeHandle] = tabWidget;
                SwitchToLoadedTab(tabWidget, userDatas);
            }
            else
            {
                Debug.LogError(ZString.Format("Tab load failed: {0}", Type.GetTypeFromHandle(typeHandle)?.Name));
            }
        }

        private void SwitchToLoadedTab(UIWidget targetTab, params System.Object[] userDatas)
        {
            if (_activeTab == targetTab) return;

            _activeTab?.Close();
            _activeTab = targetTab;
            targetTab.Open(userDatas);
        }

        private bool ValidateIndex(int index)
        {
            if (index >= 0 && index < _typeOrder.Count) return true;

            Debug.LogError(ZString.Format("Invalid tab index: {0}", index));
            return false;
        }

    }
}
