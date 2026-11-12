namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        /// <summary>窗口进入缓存：清掉参数；有限缓存时长的开始计时，常驻缓存不计时。</summary>
        private void AddToCache(UIWindow window)
        {
            window.ClearUserData();
            int cacheTime = window.MetaInfo.CacheTime;
            if (cacheTime > 0)
                window.CacheTimer = _timerService.AddTimer(_onCacheExpired, window, cacheTime, isLoop: false, isUnscaled: true);
        }

        private void RemoveFromCache(UIWindow window)
        {
            if (window.CacheTimer == 0) return;
            _timerService.RemoveTimer(window.CacheTimer);
            window.CacheTimer = 0;
        }

        private void OnCacheExpired(UIWindow window)
        {
            window.CacheTimer = 0;
            DestroyWindow(window);
        }
    }
}
