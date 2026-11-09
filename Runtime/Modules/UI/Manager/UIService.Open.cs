using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        /// <summary>
        /// 打开窗口：不存在则创建并开始加载，已存在则打开（关闭中会反向打开，已打开则刷新）。
        /// 加载中的窗口只记录参数，加载完成后打开。
        /// </summary>
        private UIWindow OpenWindow(UIMetaRegistry.UIMetaInfo info, object userData, bool sync)
        {
            if (_lifetime != UILifetime.Running)
            {
                Log.Error("[UI] UIService is not running.");
                return null;
            }
            if (info == null) return null;
            if (!info.IsWindow)
            {
                Log.Error("[UI] The UI type must be a window: {0}.", info.ViewType);
                return null;
            }
            if (_windows.TryGetValue(info.Handle, out UIWindow window))
            {
                if (sync && window.State == UIState.Loading)
                {
                    Log.Warning("[UI] {0} is loading asynchronously, ShowUISync returns null.", info.Name);
                    return null;
                }
                RemoveFromCache(window);
                BringToFront(window);
                window.OpenView(userData);
                return window;
            }
            window = (UIWindow)CreateView(info);
            if (window == null) return null;
            _windows.Add(info.Handle, window);
            BringToFront(window);
            window.OpenView(userData);
            RectTransform layer = _layers[(int)info.Layer];
            if (sync) window.LoadHolder(layer, true);
            else window.LoadHolderAsync(layer, true, CancellationToken.None).Forget();
            return window;
        }

        private UIWindow ShowWindow(UIMetaRegistry.UIMetaInfo info, object userData, bool sync)
        {
            UIWindow window = OpenWindow(info, userData, sync);
            if (window != null && window.MetaInfo.Handle.Equals(_pendingHandle)) _pendingHandle = default;
            return window;
        }

        private UniTask<T> ShowAsync<T>(UIMetaRegistry.UIMetaInfo info, object userData, CancellationToken cancellationToken)
            where T : UIWindow
        {
            if (cancellationToken.IsCancellationRequested) return UniTask.FromResult<T>(null);
            UIWindow window = ShowWindow(info, userData, sync: false);
            if (window == null) return UniTask.FromResult<T>(null);
            return window.State == UIState.Loading
                ? AwaitShown<T>(window, cancellationToken)
                : UniTask.FromResult(ShownOrNull<T>(window));
        }

        /// <summary>令牌取消时只结束本次调用的等待并返回 null，窗口照常加载。</summary>
        private static async UniTask<T> AwaitShown<T>(UIWindow window, CancellationToken cancellationToken) where T : UIWindow
        {
            try
            {
                await window.AwaitLoaded(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            return ShownOrNull<T>(window);
        }

        private static T ShownOrNull<T>(UIWindow window) where T : UIWindow =>
            window.State is UIState.Opening or UIState.Opened ? (T)window : null;

        /// <summary>创建视图实例并接入本服务。视图类型必须有 public 无参构造（源生成器保证）。</summary>
        internal UIBase CreateView(UIMetaRegistry.UIMetaInfo info)
        {
            UIBase view;
            try
            {
                view = (UIBase)Utility.InstanceFactory.CreateInstanceOptimized(info.ViewType);
            }
            catch (Exception error)
            {
                Log.Error("[UI] Failed to create {0}: {1}", info.ViewType, error);
                return null;
            }
            // 缺少 public 无参构造时 InstanceFactory 已记录原因
            if (view == null) return null;
            view.Service = this;
            view.MetaInfo = info;
            return view;
        }

        /// <summary>
        /// 关闭窗口；返回值可等待到关闭转场结束、窗口已进入缓存或被销毁。加载中关闭即取消加载并销毁；缓存中的窗口 force 时直接销毁。
        /// </summary>
        internal UIViewTransition CloseWindow(UIWindow window, bool force, bool snap, bool affectNavigation = true)
        {
            if (affectNavigation && IsCurrentPage(window)) ClearHistory();
            switch (window.State)
            {
                case UIState.Loading:
                    DestroyWindow(window);
                    break;
                case UIState.Closed:
                    if (force) DestroyWindow(window);
                    break;
                default:
                    window.CloseView(snap, force);
                    break;
            }
            return new UIViewTransition(window);
        }

        internal void ReleaseClosedWindow(UIWindow window, bool destroy)
        {
            if (destroy || window.MetaInfo.CacheTime == UICacheTime.None)
            {
                DestroyWindow(window);
                return;
            }
            RemoveFromLayer(window);
            AddToCache(window);
        }

        /// <summary>销毁窗口：先出栈、出缓存、清理导航，再销毁实例。</summary>
        internal void DestroyWindow(UIWindow window)
        {
            _windows.Remove(window.MetaInfo.Handle);
            RemoveFromLayer(window);
            RemoveFromCache(window);
            if (IsCurrentPage(window)) ClearHistory();
            window.DestroyView(true);
        }
    }
}
