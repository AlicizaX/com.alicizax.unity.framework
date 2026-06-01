using AlicizaX;
using UnityEngine.SceneManagement;

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

    public delegate void LoadSceneSuccessCallback(string sceneAssetName, UnityEngine.SceneManagement.Scene scene, float duration, object userData);
    public delegate void LoadSceneFailureCallback(string sceneAssetName, LoadResourceStatus status, string errorMessage, object userData);
    public delegate void LoadSceneUpdateCallback(string sceneAssetName, float progress, object userData);

    public delegate void UnloadSceneSuccessCallback(string sceneAssetName, object userData);
    public delegate void UnloadSceneFailureCallback(string sceneAssetName, object userData);

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

    public sealed class LoadSceneCallbacks
    {
        private readonly LoadSceneSuccessCallback m_LoadSceneSuccessCallback;
        private readonly LoadSceneFailureCallback m_LoadSceneFailureCallback;
        private readonly LoadSceneUpdateCallback m_LoadSceneUpdateCallback;

        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback)
            : this(loadSceneSuccessCallback, null, null)
        {
        }

        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback, LoadSceneFailureCallback loadSceneFailureCallback)
            : this(loadSceneSuccessCallback, loadSceneFailureCallback, null)
        {
        }

        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback, LoadSceneUpdateCallback loadSceneUpdateCallback)
            : this(loadSceneSuccessCallback, null, loadSceneUpdateCallback)
        {
        }

        public LoadSceneCallbacks(LoadSceneSuccessCallback loadSceneSuccessCallback, LoadSceneFailureCallback loadSceneFailureCallback, LoadSceneUpdateCallback loadSceneUpdateCallback)
        {
            if (loadSceneSuccessCallback == null)
            {
                throw new GameFrameworkException("Load scene success callback is invalid.");
            }

            m_LoadSceneSuccessCallback = loadSceneSuccessCallback;
            m_LoadSceneFailureCallback = loadSceneFailureCallback;
            m_LoadSceneUpdateCallback = loadSceneUpdateCallback;
        }

        public LoadSceneSuccessCallback LoadSceneSuccessCallback => m_LoadSceneSuccessCallback;
        public LoadSceneFailureCallback LoadSceneFailureCallback => m_LoadSceneFailureCallback;
        public LoadSceneUpdateCallback LoadSceneUpdateCallback => m_LoadSceneUpdateCallback;
    }

    public sealed class UnloadSceneCallbacks
    {
        private readonly UnloadSceneSuccessCallback m_UnloadSceneSuccessCallback;
        private readonly UnloadSceneFailureCallback m_UnloadSceneFailureCallback;

        public UnloadSceneCallbacks(UnloadSceneSuccessCallback unloadSceneSuccessCallback)
            : this(unloadSceneSuccessCallback, null)
        {
        }

        public UnloadSceneCallbacks(UnloadSceneSuccessCallback unloadSceneSuccessCallback, UnloadSceneFailureCallback unloadSceneFailureCallback)
        {
            if (unloadSceneSuccessCallback == null)
            {
                throw new GameFrameworkException("Unload scene success callback is invalid.");
            }

            m_UnloadSceneSuccessCallback = unloadSceneSuccessCallback;
            m_UnloadSceneFailureCallback = unloadSceneFailureCallback;
        }

        public UnloadSceneSuccessCallback UnloadSceneSuccessCallback => m_UnloadSceneSuccessCallback;
        public UnloadSceneFailureCallback UnloadSceneFailureCallback => m_UnloadSceneFailureCallback;
    }
}
