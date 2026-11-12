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
            if (_stopped)
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
            _windows.Add(info.Handle, window);
            BringToFront(window);
            window.OpenView(userData);
            RectTransform layer = _layers[(int)info.Layer];
            if (sync) window.LoadHolder(layer, true);
            else window.LoadHolderAsync(layer, true, CancellationToken.None).Forget();
            return window;
        }

        /// <summary>ShowUI 的打开：导航正等着提交的若是同一个窗口，改由 ShowUI 接手。</summary>
        private UIWindow ShowWindow(UIMetaRegistry.UIMetaInfo info, object userData, bool sync)
        {
            UIWindow window = OpenWindow(info, userData, sync);
            if (window != null) _navigator.Release(window);
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
        private static async UniTask<T> AwaitShown<T>(UIWindow window, CancellationToken cancellationToken) where T : UIWindow =>
            await window.AwaitLoaded(cancellationToken).SuppressCancellationThrow() ? null : ShownOrNull<T>(window);

        private static T ShownOrNull<T>(UIWindow window) where T : UIWindow =>
            window.State is UIState.Opening or UIState.Opened ? (T)window : null;

        /// <summary>用注册时给出的工厂创建视图实例并接入本服务。</summary>
        internal UIBase CreateView(UIMetaRegistry.UIMetaInfo info)
        {
            UIBase view = info.Create();
            view.Service = this;
            view.MetaInfo = info;
            return view;
        }

        /// <summary>
        /// 业务侧的关闭（CloseUI、CloseSelf、TryCloseTop）：关的是导航当前页时，导航历史随之清空。
        /// 返回值可等待到关闭转场结束、窗口已进入缓存或被销毁。
        /// </summary>
        internal UIViewTransition CloseWindow(UIWindow window, bool force, bool snap)
        {
            _navigator.Forget(window);
            return Close(window, force, snap);
        }

        /// <summary>
        /// 只关闭窗口，不动导航历史。加载中关闭即取消加载并销毁；缓存中的窗口 force 时直接销毁。
        /// </summary>
        private UIViewTransition Close(UIWindow window, bool force, bool snap)
        {
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

        /// <summary>窗口自身失效（加载失败、节点被外部销毁）时销毁它；它是导航当前页时，导航历史随之清空。</summary>
        internal void DestroyLostWindow(UIWindow window)
        {
            _navigator.Forget(window);
            DestroyWindow(window);
        }

        /// <summary>销毁窗口：先出层级、出缓存，再销毁实例。不动导航历史。</summary>
        internal void DestroyWindow(UIWindow window)
        {
            _windows.Remove(window.MetaInfo.Handle);
            RemoveFromLayer(window);
            RemoveFromCache(window);
            window.DestroyView(true);
        }
    }
}
