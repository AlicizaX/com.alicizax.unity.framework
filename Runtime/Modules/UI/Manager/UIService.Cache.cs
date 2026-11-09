namespace AlicizaX.UI.Runtime
{
    internal sealed partial class UIService
    {
        private void AddToCache(UIWindow window)
        {
            window.ClearUserData();
            int cacheTime = window.MetaInfo.CacheTime;
            if (cacheTime <= 0) return;
            window.CacheTimer = _timerService.AddTimer(_onCacheExpired, window, cacheTime, isLoop: false, isUnscaled: true);
            if (window.CacheTimer == 0) DestroyWindow(window);
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
