using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    /// <summary>
    /// 页签窗口：未加载的页签在首次切换时异步创建。<see cref="SwitchTab"/> 被更新的切换取代时返回 null。
    /// </summary>
    public abstract class UITabWindow<T> : UIWindow<T> where T : UIHolderObjectBase
    {
        private sealed class TabEntry
        {
            internal readonly UIMetaRegistry.UIMetaInfo Info;
            internal readonly Transform Parent;
            internal UIWidget View;

            internal TabEntry(UIMetaRegistry.UIMetaInfo info, Transform parent)
            {
                Info = info;
                Parent = parent;
            }
        }

        private readonly List<TabEntry> _tabs = new();
        private int _activeIndex = -1;
        private int _switchVersion;

        public UIWidget ActiveTab => _activeIndex >= 0 ? _tabs[_activeIndex].View : null;

        protected void InitTabVirtuallyView<TTab>(Transform parent = null) where TTab : UIWidget =>
            AddTab(UIMetaRegistry.Require(typeof(TTab)), parent);

        protected void InitTabVirtuallyView(string typeName, Transform parent = null) =>
            AddTab(RequireWidget(typeName), parent);

        /// <summary>
        /// 切到指定页签；结果为本次切换生效后的页签。已加载页签之间切换同步完成且不分配。
        /// </summary>
        public async UniTask<UIWidget> SwitchTab(int index, object userData = null)
        {
            if ((uint)index >= (uint)_tabs.Count)
            {
                Log.Error("[UI] Invalid tab index: {0}", index);
                return null;
            }
            int version = ++_switchVersion;
            TabEntry entry = _tabs[index];
            if (entry.View == null || entry.View.State == UIState.Destroyed)
            {
                entry.View = AddWidget(entry.Info, entry.Parent);
                if (entry.View == null) return null;
                entry.View.LoadHolderAsync(entry.Parent, false, CancellationToken.None).Forget();
            }
            await entry.View.AwaitLoaded();
            if (version != _switchVersion || entry.View.State == UIState.Destroyed) return null;
            if (_activeIndex >= 0 && _activeIndex != index) _tabs[_activeIndex].View.Close();
            _activeIndex = index;
            entry.View.Open(userData);
            return entry.View;
        }

        private void AddTab(UIMetaRegistry.UIMetaInfo info, Transform parent)
        {
            if (info != null) _tabs.Add(new TabEntry(info, parent != null ? parent : baseui.RectTransform));
        }
    }
}
