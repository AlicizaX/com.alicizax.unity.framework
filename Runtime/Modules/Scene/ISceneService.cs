using System;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace AlicizaX.Scene.Runtime
{
    public interface ISceneService : IService
    {
        string CurrentMainSceneName { get; }

        UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100, bool gcCollect = false,
            Action<float> progressCallBack = null);

        void LoadScene(string location,
            LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false,
            uint priority = 100,
            Action<UnityEngine.SceneManagement.Scene> callBack = null,
            bool gcCollect = false,
            Action<float> progressCallBack = null);

        bool ActivateScene(string location);

        bool UnSuspend(string location);

        bool IsMainScene(string location);

        UniTask<bool> UnloadSubSceneAsync(string location, bool gcCollect = false, Action<float> progressCallback = null);

        void UnloadSubScene(string location, Action callback = null, bool gcCollect = false, Action<float> progressCallback = null);

        bool IsContainScene(string location);
    }
}
