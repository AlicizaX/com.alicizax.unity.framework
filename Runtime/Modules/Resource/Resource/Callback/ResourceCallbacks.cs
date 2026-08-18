using AlicizaX;

namespace AlicizaX.Resource.Runtime
{
    public enum LoadResourceStatus : byte
    {
        Success = 0,
        NotExist,
        NotReady,
        DependencyError,
        TypeError,
        AssetError
    }

    public delegate void LoadAssetSuccessCallback(string assetName, object asset, float duration, object userData);
    public delegate void LoadAssetFailureCallback(string assetName, LoadResourceStatus status, string errorMessage, object userData);
    public delegate void LoadAssetUpdateCallback(string assetName, float progress, object userData);

    public sealed class LoadAssetCallbacks
    {
        private readonly LoadAssetSuccessCallback m_LoadAssetSuccessCallback;
        private readonly LoadAssetFailureCallback m_LoadAssetFailureCallback;
        private readonly LoadAssetUpdateCallback m_LoadAssetUpdateCallback;

        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback)
            : this(loadAssetSuccessCallback, null, null)
        {
        }

        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetFailureCallback loadAssetFailureCallback)
            : this(loadAssetSuccessCallback, loadAssetFailureCallback, null)
        {
        }

        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetUpdateCallback loadAssetUpdateCallback)
            : this(loadAssetSuccessCallback, null, loadAssetUpdateCallback)
        {
        }

        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetFailureCallback loadAssetFailureCallback, LoadAssetUpdateCallback loadAssetUpdateCallback)
        {
            if (loadAssetSuccessCallback == null)
            {
                throw new GameFrameworkException("Load asset success callback is invalid.");
            }

            m_LoadAssetSuccessCallback = loadAssetSuccessCallback;
            m_LoadAssetFailureCallback = loadAssetFailureCallback;
            m_LoadAssetUpdateCallback = loadAssetUpdateCallback;
        }

        public LoadAssetSuccessCallback LoadAssetSuccessCallback => m_LoadAssetSuccessCallback;
        public LoadAssetFailureCallback LoadAssetFailureCallback => m_LoadAssetFailureCallback;
        public LoadAssetUpdateCallback LoadAssetUpdateCallback => m_LoadAssetUpdateCallback;
    }
}
