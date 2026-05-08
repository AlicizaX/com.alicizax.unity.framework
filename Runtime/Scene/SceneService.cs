using System;
using System.Collections.Generic;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using YooAsset;

namespace AlicizaX.Scene.Runtime
{
    internal class SceneService : ServiceBase, ISceneService
    {
        public string CurrentMainSceneName => EnsureSceneState().CurrentMainSceneName;

        protected override void OnInitialize()
        {
            var activeScene = SceneManager.GetActiveScene();
            EnsureSceneState().SetBootScene(activeScene.name);
        }

        protected override void OnDestroyService()
        {
        }

        public async UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100, bool gcCollect = true, Action<float> progressCallBack = null)
        {
            var sceneState = EnsureSceneState();
            if (!sceneState.TryBeginHandling(location))
            {
                Log.Error($"Could not load scene while loading. Scene: {location}");
                return default;
            }

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (sceneState.TryGetSubScene(location, out YooAsset.SceneHandle loadedSubScene))
                {
                    throw new Exception($"Could not load subScene while already loaded. Scene: {location}");
                }

                var subScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
                sceneState.AddSubScene(location, subScene);

                if (progressCallBack != null)
                {
                    while (!subScene.IsDone && subScene.IsValid)
                    {
                        progressCallBack.Invoke(subScene.Progress);
                        await UniTask.Yield();
                    }
                }
                else
                {
                    await subScene.ToUniTask();
                }

                sceneState.EndHandling(location);
                return subScene.SceneObject;
            }

            if (sceneState.CurrentMainSceneHandle is { IsDone: false })
            {
                throw new Exception($"Could not load MainScene while loading. CurrentMainScene: {sceneState.CurrentMainSceneName}.");
            }

            sceneState = PrepareSceneStateForMainSceneLoad(location);
            var mainSceneHandle = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

            if (progressCallBack != null)
            {
                while (!mainSceneHandle.IsDone && mainSceneHandle.IsValid)
                {
                    progressCallBack.Invoke(mainSceneHandle.Progress);
                    await UniTask.Yield();
                }
            }
            else
            {
                await mainSceneHandle.ToUniTask();
            }

            sceneState.SetMainScene(location, mainSceneHandle);
            Context.Require<IResourceService>().ForceUnloadUnusedAssets(gcCollect);
            sceneState.EndHandling(location);
            return mainSceneHandle.SceneObject;
        }

        public void LoadScene(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            Action<UnityEngine.SceneManagement.Scene> callBack = null,
            bool gcCollect = true, Action<float> progressCallBack = null)
        {
            var sceneState = EnsureSceneState();
            if (!sceneState.TryBeginHandling(location))
            {
                Log.Error($"Could not load scene while loading. Scene: {location}");
                return;
            }

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (sceneState.TryGetSubScene(location, out YooAsset.SceneHandle loadedSubScene))
                {
                    Log.Warning($"Could not load subScene while already loaded. Scene: {location}");
                    return;
                }

                var subScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
                sceneState.AddSubScene(location, subScene);
                subScene.Completed += handle =>
                {
                    sceneState.EndHandling(location);
                    callBack?.Invoke(handle.SceneObject);
                };

                if (progressCallBack != null)
                {
                    InvokeSceneProgress(subScene, progressCallBack).Forget();
                }

                return;
            }

            if (sceneState.CurrentMainSceneHandle is { IsDone: false })
            {
                Log.Warning($"Could not load MainScene while loading. CurrentMainScene: {sceneState.CurrentMainSceneName}.");
                return;
            }

            sceneState = PrepareSceneStateForMainSceneLoad(location);
            var mainSceneHandle = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
            mainSceneHandle.Completed += handle =>
            {
                sceneState.SetMainScene(location, handle);
                sceneState.EndHandling(location);
                callBack?.Invoke(handle.SceneObject);
            };

            if (progressCallBack != null)
            {
                InvokeSceneProgress(mainSceneHandle, progressCallBack).Forget();
            }

            Context.Require<IResourceService>().ForceUnloadUnusedAssets(gcCollect);
        }

        public bool ActivateScene(string location)
        {
            var sceneState = EnsureSceneState();
            if (sceneState.CurrentMainSceneName.Equals(location))
            {
                return sceneState.CurrentMainSceneHandle != null && sceneState.CurrentMainSceneHandle.ActivateScene();
            }

            if (sceneState.TryGetSubScene(location, out var subScene) && subScene != null)
            {
                return subScene.ActivateScene();
            }

            Log.Warning($"IsMainScene invalid location:{location}");
            return false;
        }


        public bool UnSuspend(string location)
        {
            var sceneState = EnsureSceneState();
            if (sceneState.CurrentMainSceneName.Equals(location))
            {
                return sceneState.CurrentMainSceneHandle != null && sceneState.CurrentMainSceneHandle.UnSuspend();
            }

            if (sceneState.TryGetSubScene(location, out var subScene) && subScene != null)
            {
                return subScene.UnSuspend();
            }

            Log.Warning($"IsMainScene invalid location:{location}");
            return false;
        }

        public bool IsMainScene(string location)
        {
            var sceneState = EnsureSceneState();
            var currentScene = SceneManager.GetActiveScene();

            if (sceneState.CurrentMainSceneName.Equals(location))
            {
                if (sceneState.CurrentMainSceneHandle == null)
                {
                    return currentScene.name == location;
                }

                if (currentScene.name == sceneState.CurrentMainSceneHandle.SceneName)
                {
                    return true;
                }

                return sceneState.CurrentMainSceneHandle.SceneName == currentScene.name;
            }

            if (currentScene.name == sceneState.CurrentMainSceneHandle?.SceneName)
            {
                return true;
            }

            Log.Warning($"IsMainScene invalid location:{location}");
            return false;
        }

        public async UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null)
        {
            var sceneState = EnsureSceneState();
            if (sceneState.TryGetSubScene(location, out var subScene) && subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    Log.Error($"Could not unload Scene while not loaded. Scene: {location}");
                    return false;
                }

                if (!sceneState.TryBeginHandling(location))
                {
                    Log.Warning($"Could not unload Scene while loading. Scene: {location}");
                    return false;
                }

                var unloadOperation = subScene.UnloadAsync();
                if (progressCallBack != null)
                {
                    while (!unloadOperation.IsDone && unloadOperation.Status != EOperationStatus.Failed)
                    {
                        progressCallBack.Invoke(unloadOperation.Progress);
                        await UniTask.Yield();
                    }
                }
                else
                {
                    await unloadOperation.ToUniTask();
                }

                sceneState.RemoveSubScene(location);
                sceneState.EndHandling(location);
                return true;
            }

            Log.Warning($"UnloadAsync invalid location:{location}");
            return false;
        }

        public void Unload(string location, Action callBack = null, Action<float> progressCallBack = null)
        {
            var sceneState = EnsureSceneState();
            if (sceneState.TryGetSubScene(location, out var subScene) && subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    Log.Error($"Could not unload Scene while not loaded. Scene: {location}");
                    return;
                }

                if (!sceneState.TryBeginHandling(location))
                {
                    Log.Warning($"Could not unload Scene while loading. Scene: {location}");
                    return;
                }

                var unloadOperation = subScene.UnloadAsync();
                unloadOperation.Completed += @base =>
                {
                    sceneState.RemoveSubScene(location);
                    sceneState.EndHandling(location);
                    callBack?.Invoke();
                };

                if (progressCallBack != null)
                {
                    InvokeOperationProgress(unloadOperation, progressCallBack).Forget();
                }

                return;
            }

            Log.Warning($"UnloadAsync invalid location:{location}");
        }

        public bool IsContainScene(string location)
        {
            return EnsureSceneState().IsContainScene(location);
        }

        private SceneDomainStateService EnsureSceneState()
        {
            var sceneScope = Context.EnsureScene();
            if (!sceneScope.TryGet<SceneDomainStateService>(out var sceneState))
            {
                sceneState = (SceneDomainStateService)sceneScope.Register<ISceneStateService>(new SceneDomainStateService());
            }

            return sceneState;
        }

        private SceneDomainStateService PrepareSceneStateForMainSceneLoad(string location)
        {
            var sceneScope = Context.ResetScene();
            var sceneState = (SceneDomainStateService)sceneScope.Register<ISceneStateService>(new SceneDomainStateService());
            sceneState.MarkMainSceneLoading(location);
            return sceneState;
        }

        private async UniTaskVoid InvokeSceneProgress(YooAsset.SceneHandle sceneHandle, Action<float> progress)
        {
            if (sceneHandle == null)
            {
                return;
            }

            while (!sceneHandle.IsDone && sceneHandle.IsValid)
            {
                await UniTask.Yield();
                progress?.Invoke(sceneHandle.Progress);
            }
        }

        private async UniTaskVoid InvokeOperationProgress(AsyncOperationBase operation, Action<float> progress)
        {
            if (operation == null)
            {
                return;
            }

            while (!operation.IsDone && operation.Status != EOperationStatus.Failed)
            {
                await UniTask.Yield();
                progress?.Invoke(operation.Progress);
            }
        }
    }

    internal sealed class SceneDomainStateService : ServiceBase, ISceneStateService
    {
        private readonly Dictionary<string,YooAsset.SceneHandle> _subScenes = new ();
        private readonly HashSet<string> _handlingScenes = new HashSet<string>();

        public string CurrentMainSceneName { get; private set; } = string.Empty;

        public YooAsset.SceneHandle CurrentMainSceneHandle { get; private set; }

        protected override void OnInitialize()
        {
        }

        protected override void OnDestroyService()
        {
            foreach (var subScene in _subScenes.Values)
            {
                subScene?.UnloadAsync();
            }

            _subScenes.Clear();
            _handlingScenes.Clear();
            CurrentMainSceneHandle = null;
            CurrentMainSceneName = string.Empty;
        }

        public void SetBootScene(string sceneName)
        {
            CurrentMainSceneName = sceneName ?? string.Empty;
            CurrentMainSceneHandle = null;
            _subScenes.Clear();
            _handlingScenes.Clear();
        }

        public void MarkMainSceneLoading(string sceneName)
        {
            CurrentMainSceneName = sceneName ?? string.Empty;
            CurrentMainSceneHandle = null;
            _handlingScenes.Clear();
            TryBeginHandling(sceneName);
        }

        public void SetMainScene(string sceneName, YooAsset.SceneHandle sceneHandle)
        {
            CurrentMainSceneName = sceneName ?? string.Empty;
            CurrentMainSceneHandle = sceneHandle;
        }

        public bool TryBeginHandling(string location)
            => !string.IsNullOrEmpty(location) && _handlingScenes.Add(location);

        public void EndHandling(string location)
        {
            if (!string.IsNullOrEmpty(location))
            {
                _handlingScenes.Remove(location);
            }
        }

        public void AddSubScene(string location, YooAsset.SceneHandle sceneHandle)
        {
            _subScenes[location] = sceneHandle;
        }

        public bool TryGetSubScene(string location, out YooAsset.SceneHandle sceneHandle)
            => _subScenes.TryGetValue(location, out sceneHandle);

        public bool RemoveSubScene(string location)
            => _subScenes.Remove(location);

        public bool IsContainScene(string location)
        {
            if (CurrentMainSceneName.Equals(location))
            {
                return true;
            }

            return _subScenes.ContainsKey(location);
        }

        public bool IsMainScene(string location)
            => CurrentMainSceneName.Equals(location);
    }
}
