using System;
using AlicizaX;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace AlicizaX.Scene.Runtime
{
    public interface ISceneService : IService
    {
        public string CurrentMainSceneName { get; }

        public UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100, bool gcCollect = true,
            Action<float> progressCallBack = null);

        public void LoadScene(string location,
            LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false,
            uint priority = 100,
            Action<UnityEngine.SceneManagement.Scene> callBack = null,
            bool gcCollect = true,
            Action<float> progressCallBack = null);

        public bool ActivateScene(string location);

        public bool UnSuspend(string location);

        public bool IsMainScene(string location);

        public UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null);

        public void Unload(string location, Action callBack = null, Action<float> progressCallBack = null);

        public bool IsContainScene(string location);
    }

    public interface ISceneStateService : IService
    {
        public string CurrentMainSceneName { get; }

        public bool IsContainScene(string location);

        public bool IsMainScene(string location);
    }
}
