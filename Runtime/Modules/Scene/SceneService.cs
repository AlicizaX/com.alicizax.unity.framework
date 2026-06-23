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
        private readonly SceneDomainState _sceneState = new();
        private int _lifecycleVersion;
        private bool _destroyed = true;

        public string CurrentMainSceneName => _sceneState.CurrentMainSceneName;

        protected override void OnInitialize()
        {
            unchecked
            {
                _lifecycleVersion++;
            }

            _destroyed = false;
            var activeScene = SceneManager.GetActiveScene();
            _sceneState.SetBootScene(activeScene.name);
        }

        protected override void OnDestroyService()
        {
            _destroyed = true;
            unchecked
            {
                _lifecycleVersion++;
            }

            _sceneState.Destroy();
        }

        public UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false,
            uint priority = 100, bool gcCollect = false, Action<float> progressCallback = null)
        {
            return sceneMode == LoadSceneMode.Additive
                ? LoadSubSceneAsync(location, suspendLoad, priority, progressCallback)
                : LoadMainSceneAsync(location, sceneMode, suspendLoad, priority, gcCollect, progressCallback);
        }

        public void LoadScene(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            Action<UnityEngine.SceneManagement.Scene> callback = null,
            bool gcCollect = false, Action<float> progressCallback = null)
        {
            LoadSceneCallbackAsync(location, sceneMode, suspendLoad, priority, callback, gcCollect, progressCallback).Forget();
        }

        public bool ActivateScene(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                Log.Warning("ActivateScene invalid location.");
                return false;
            }

            var sceneState = _sceneState;
            if (sceneState.IsMainScene(location))
            {
                var mainSceneHandle = sceneState.CurrentMainSceneHandle;
                if (mainSceneHandle != null)
                {
                    return mainSceneHandle.ActivateScene();
                }

                var scene = SceneManager.GetSceneByName(location);
                return scene.IsValid() && scene.isLoaded && SceneManager.SetActiveScene(scene);
            }

            if (sceneState.TryGetSubScene(location, out var subScene) && subScene != null)
            {
                return subScene.ActivateScene();
            }

            Log.Warning($"ActivateScene invalid location:{location}");
            return false;
        }

        public bool UnSuspend(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                Log.Warning("UnSuspend invalid location.");
                return false;
            }

            if (_sceneState.TryGetAnySceneHandle(location, out var sceneHandle) && sceneHandle != null)
            {
                return sceneHandle.UnSuspend();
            }

            Log.Warning($"UnSuspend invalid location:{location}");
            return false;
        }

        public bool IsMainScene(string location)
        {
            return _sceneState.IsMainScene(location);
        }

        public async UniTask<bool> UnloadSubSceneAsync(string location, bool gcCollect = false, Action<float> progressCallback = null)
        {
            if (string.IsNullOrEmpty(location))
            {
                Log.Warning("UnloadSubSceneAsync invalid location.");
                return false;
            }

            var lifecycleVersion = _lifecycleVersion;
            var sceneState = _sceneState;
            if (!sceneState.TryBeginSubSceneUnload(location, out var subScene))
            {
                Log.Warning($"Could not unload sub scene. Scene: {location}");
                return false;
            }

            bool succeeded = false;
            AsyncOperationBase unloadOperation = null;
            try
            {
                unloadOperation = subScene.UnloadAsync();
                await AwaitOperation(unloadOperation, progressCallback);
                if (!IsServiceAlive(lifecycleVersion))
                {
                    return false;
                }

                succeeded = unloadOperation.Status == EOperationStatus.Succeed;
                if (!succeeded)
                {
                    Log.Error($"Unload sub scene failed. Scene: {location}, Error: {GetOperationError(unloadOperation)}");
                    return false;
                }

                RequestUnloadUnusedAssets(gcCollect);
                InvokeProgress(progressCallback, 1f);
                return true;
            }
            catch (Exception exception)
            {
                LogOperationException($"Unload sub scene failed. Scene: {location}", exception, GetOperationError(unloadOperation));
                return false;
            }
            finally
            {
                if (IsServiceAlive(lifecycleVersion))
                {
                    sceneState.EndSubSceneUnload(location, subScene, succeeded);
                }
            }
        }

        public void UnloadSubScene(string location, Action callback = null, bool gcCollect = false, Action<float> progressCallback = null)
        {
            UnloadCallbackAsync(location, callback, gcCollect, progressCallback).Forget();
        }

        public bool IsContainScene(string location)
        {
            return _sceneState.IsContainScene(location);
        }

        private async UniTask<UnityEngine.SceneManagement.Scene> LoadMainSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority,
            bool gcCollect, Action<float> progressCallback)
        {
            if (string.IsNullOrEmpty(location))
            {
                Log.Error("Load main scene failed. Location is invalid.");
                return default;
            }

            var lifecycleVersion = _lifecycleVersion;
            var sceneState = _sceneState;
            if (!sceneState.TryBeginMainSceneLoad(location))
            {
                Log.Warning($"Could not load main scene while another scene operation is running. Scene: {location}");
                return default;
            }

            YooAsset.SceneHandle mainSceneHandle = null;
            bool succeeded = false;
            bool operationAbandoned = false;
            try
            {
                mainSceneHandle = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
                sceneState.SetLoadingMainScene(location, mainSceneHandle);

                await AwaitSceneHandle(mainSceneHandle, progressCallback);
                if (!IsServiceAlive(lifecycleVersion))
                {
                    operationAbandoned = true;
                    return default;
                }

                succeeded = IsSceneLoadSucceed(mainSceneHandle);
                if (!succeeded)
                {
                    Log.Error($"Load main scene failed. Scene: {location}, Error: {GetSceneHandleError(mainSceneHandle)}");
                    return default;
                }

                var scene = mainSceneHandle.SceneObject;
                sceneState.SetMainScene(location, mainSceneHandle);
                RequestUnloadUnusedAssets(gcCollect);
                InvokeProgress(progressCallback, 1f);
                return scene;
            }
            catch (Exception exception)
            {
                LogOperationException($"Load main scene failed. Scene: {location}", exception, GetSceneHandleError(mainSceneHandle));
                return default;
            }
            finally
            {
                if (!succeeded && !operationAbandoned)
                {
                    ReleaseSceneHandle(mainSceneHandle);
                }

                if (IsServiceAlive(lifecycleVersion))
                {
                    sceneState.EndMainSceneLoad(location);
                }
            }
        }

        private async UniTask<UnityEngine.SceneManagement.Scene> LoadSubSceneAsync(string location, bool suspendLoad, uint priority, Action<float> progressCallback)
        {
            if (string.IsNullOrEmpty(location))
            {
                Log.Error("Load sub scene failed. Location is invalid.");
                return default;
            }

            var lifecycleVersion = _lifecycleVersion;
            var sceneState = _sceneState;
            if (!sceneState.TryBeginSubSceneLoad(location))
            {
                Log.Warning($"Could not load sub scene while scene is loaded or another scene operation is running. Scene: {location}");
                return default;
            }

            YooAsset.SceneHandle subSceneHandle = null;
            bool succeeded = false;
            bool operationAbandoned = false;
            try
            {
                subSceneHandle = YooAssets.LoadSceneAsync(location, LoadSceneMode.Additive, LocalPhysicsMode.None, suspendLoad, priority);
                sceneState.SetSubSceneLoading(location, subSceneHandle);

                await AwaitSceneHandle(subSceneHandle, progressCallback);
                if (!IsServiceAlive(lifecycleVersion))
                {
                    operationAbandoned = true;
                    return default;
                }

                succeeded = IsSceneLoadSucceed(subSceneHandle);
                if (!succeeded)
                {
                    Log.Error($"Load sub scene failed. Scene: {location}, Error: {GetSceneHandleError(subSceneHandle)}");
                    return default;
                }

                sceneState.SetSubSceneLoaded(location, subSceneHandle);
                InvokeProgress(progressCallback, 1f);
                return subSceneHandle.SceneObject;
            }
            catch (Exception exception)
            {
                LogOperationException($"Load sub scene failed. Scene: {location}", exception, GetSceneHandleError(subSceneHandle));
                return default;
            }
            finally
            {
                if (!succeeded && !operationAbandoned)
                {
                    sceneState.RemoveSubScene(location);
                    ReleaseSceneHandle(subSceneHandle);
                }

                if (IsServiceAlive(lifecycleVersion))
                {
                    sceneState.EndHandling(location);
                }
            }
        }

        private async UniTaskVoid LoadSceneCallbackAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority,
            Action<UnityEngine.SceneManagement.Scene> callback, bool gcCollect, Action<float> progressCallback)
        {
            try
            {
                var scene = await LoadSceneAsync(location, sceneMode, suspendLoad, priority, gcCollect, progressCallback);
                if (scene.IsValid())
                {
                    InvokeSceneCallback(callback, scene);
                }
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
        }

        private async UniTaskVoid UnloadCallbackAsync(string location, Action callback, bool gcCollect, Action<float> progressCallback)
        {
            try
            {
                bool succeeded = await UnloadSubSceneAsync(location, gcCollect, progressCallback);
                if (succeeded)
                {
                    InvokeCallback(callback);
                }
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
        }

        private async UniTask AwaitSceneHandle(YooAsset.SceneHandle sceneHandle, Action<float> progress)
        {
            if (sceneHandle == null)
            {
                return;
            }

            if (progress == null)
            {
                await sceneHandle.ToUniTask();
                return;
            }

            InvokeProgress(progress, 0f);
            await sceneHandle.ToUniTask(CreateProgress(progress));
        }

        private async UniTask AwaitOperation(AsyncOperationBase operation, Action<float> progress)
        {
            if (operation == null)
            {
                return;
            }

            if (progress == null)
            {
                await operation.ToUniTask();
                return;
            }

            InvokeProgress(progress, 0f);
            await operation.ToUniTask(CreateProgress(progress));
        }

        private IProgress<float> CreateProgress(Action<float> progress)
        {
            return Cysharp.Threading.Tasks.Progress.Create<float>(value => InvokeProgress(progress, value));
        }

        private void InvokeProgress(Action<float> progress, float value)
        {
            if (progress == null)
            {
                return;
            }

            try
            {
                progress.Invoke(value);
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
        }

        private bool IsServiceAlive(int lifecycleVersion)
        {
            return !_destroyed && lifecycleVersion == _lifecycleVersion;
        }

        private void InvokeCallback(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback.Invoke();
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
        }

        private void InvokeSceneCallback(Action<UnityEngine.SceneManagement.Scene> callback, UnityEngine.SceneManagement.Scene scene)
        {
            if (callback == null)
            {
                return;
            }

            try
            {
                callback.Invoke(scene);
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
        }

        private void RequestUnloadUnusedAssets(bool gcCollect)
        {
            try
            {
                Require<IResourceService>().ForceUnloadUnusedAssets(gcCollect);
            }
            catch (Exception exception)
            {
                Log.Exception(exception);
            }
        }

        private static void LogOperationException(string message, Exception exception, string operationError)
        {
            if (!string.IsNullOrEmpty(operationError))
            {
                Log.Error($"{message}, Error: {operationError}");
            }
            else
            {
                Log.Error(message);
            }

            Log.Exception(exception);
        }

        private static bool IsSceneLoadSucceed(YooAsset.SceneHandle sceneHandle)
        {
            if (sceneHandle is not { IsValid: true, Status: EOperationStatus.Succeed })
            {
                return false;
            }

            var scene = sceneHandle.SceneObject;
            return scene.IsValid() && scene.isLoaded;
        }

        private static bool IsSceneHandleLoaded(YooAsset.SceneHandle sceneHandle)
        {
            if (sceneHandle == null || !sceneHandle.IsValid)
            {
                return false;
            }

            var scene = sceneHandle.SceneObject;
            return scene.IsValid() && scene.isLoaded;
        }

        private static string GetSceneHandleError(YooAsset.SceneHandle sceneHandle)
        {
            return sceneHandle != null && sceneHandle.IsValid ? sceneHandle.LastError : "Scene handle is invalid.";
        }

        private static string GetOperationError(AsyncOperationBase operation)
        {
            return operation != null ? operation.Error : "Operation is invalid.";
        }

        private static void ReleaseSceneHandle(YooAsset.SceneHandle sceneHandle)
        {
            if (sceneHandle != null && sceneHandle.IsValid)
            {
                sceneHandle.Release();
            }
        }

        internal sealed class SceneDomainState
        {
            private readonly Dictionary<string, SceneSlot> _subScenes = new();
            private readonly HashSet<string> _handlingScenes = new();

            private string _loadingMainSceneName = string.Empty;
            private YooAsset.SceneHandle _loadingMainSceneHandle;
            private bool _mainSceneSwitching;

            public string CurrentMainSceneName { get; private set; } = string.Empty;

            public YooAsset.SceneHandle CurrentMainSceneHandle { get; private set; }

            public void Destroy()
            {
                if (_loadingMainSceneHandle != CurrentMainSceneHandle)
                {
                    TryUnloadSceneHandle(_loadingMainSceneHandle);
                }

                UnloadSubScenes();
                _subScenes.Clear();
                _handlingScenes.Clear();
                _loadingMainSceneName = string.Empty;
                _loadingMainSceneHandle = null;
                _mainSceneSwitching = false;
                CurrentMainSceneHandle = null;
                CurrentMainSceneName = string.Empty;
            }

            private static void TryUnloadSceneHandle(YooAsset.SceneHandle sceneHandle)
            {
                if (sceneHandle == null || !sceneHandle.IsValid)
                {
                    return;
                }

                try
                {
                    var scene = sceneHandle.SceneObject;
                    if (sceneHandle.IsDone && (!scene.IsValid() || !scene.isLoaded))
                    {
                        SceneService.ReleaseSceneHandle(sceneHandle);
                        return;
                    }

                    sceneHandle.UnloadAsync();
                }
                catch (Exception exception)
                {
                    Log.Exception(exception);
                }
            }

            private void UnloadSubScenes()
            {
                foreach (var subScene in _subScenes.Values)
                {
                    if (subScene.State == SceneLoadState.Unloading)
                    {
                        continue;
                    }

                    TryUnloadSceneHandle(subScene.Handle);
                }
            }

            public void SetBootScene(string sceneName)
            {
                CurrentMainSceneName = sceneName ?? string.Empty;
                CurrentMainSceneHandle = null;
                _loadingMainSceneName = string.Empty;
                _loadingMainSceneHandle = null;
                _mainSceneSwitching = false;
                _subScenes.Clear();
                _handlingScenes.Clear();
            }

            public bool TryBeginMainSceneLoad(string location)
            {
                if (string.IsNullOrEmpty(location) || _mainSceneSwitching || _handlingScenes.Count > 0)
                {
                    return false;
                }

                _mainSceneSwitching = true;
                _loadingMainSceneName = location;
                return true;
            }

            public void SetLoadingMainScene(string sceneName, YooAsset.SceneHandle sceneHandle)
            {
                _loadingMainSceneName = sceneName ?? string.Empty;
                _loadingMainSceneHandle = sceneHandle;
            }

            public void SetMainScene(string sceneName, YooAsset.SceneHandle sceneHandle)
            {
                CurrentMainSceneName = sceneName ?? string.Empty;
                CurrentMainSceneHandle = sceneHandle;
                UnloadSubScenes();
                _subScenes.Clear();
            }

            public void EndMainSceneLoad(string location)
            {
                if (string.Equals(_loadingMainSceneName, location, StringComparison.Ordinal))
                {
                    _loadingMainSceneName = string.Empty;
                    _loadingMainSceneHandle = null;
                }

                _mainSceneSwitching = false;
            }

            public bool TryBeginSubSceneLoad(string location)
            {
                if (string.IsNullOrEmpty(location) || _mainSceneSwitching || _subScenes.ContainsKey(location))
                {
                    return false;
                }

                return _handlingScenes.Add(location);
            }

            public void SetSubSceneLoading(string location, YooAsset.SceneHandle sceneHandle)
            {
                _subScenes[location] = new SceneSlot(sceneHandle, SceneLoadState.Loading);
            }

            public void SetSubSceneLoaded(string location, YooAsset.SceneHandle sceneHandle)
            {
                if (_subScenes.TryGetValue(location, out var slot) && slot.Handle == sceneHandle)
                {
                    slot.State = SceneLoadState.Loaded;
                    return;
                }

                _subScenes[location] = new SceneSlot(sceneHandle, SceneLoadState.Loaded);
            }

            public bool TryBeginSubSceneUnload(string location, out YooAsset.SceneHandle sceneHandle)
            {
                sceneHandle = null;
                if (_mainSceneSwitching || !_subScenes.TryGetValue(location, out var slot) || slot.State != SceneLoadState.Loaded || slot.Handle == null)
                {
                    return false;
                }

                if (!_handlingScenes.Add(location))
                {
                    return false;
                }

                slot.State = SceneLoadState.Unloading;
                sceneHandle = slot.Handle;
                return true;
            }

            public void EndSubSceneUnload(string location, YooAsset.SceneHandle sceneHandle, bool succeeded)
            {
                if (succeeded)
                {
                    _subScenes.Remove(location);
                }
                else if (_subScenes.TryGetValue(location, out var slot) && slot.Handle == sceneHandle)
                {
                    if (SceneService.IsSceneHandleLoaded(sceneHandle))
                    {
                        slot.State = SceneLoadState.Loaded;
                    }
                    else
                    {
                        _subScenes.Remove(location);
                        SceneService.ReleaseSceneHandle(sceneHandle);
                    }
                }

                EndHandling(location);
            }

            public void EndHandling(string location)
            {
                if (!string.IsNullOrEmpty(location))
                {
                    _handlingScenes.Remove(location);
                }
            }

            public bool TryGetSubScene(string location, out YooAsset.SceneHandle sceneHandle)
            {
                sceneHandle = null;
                if (!_subScenes.TryGetValue(location, out var slot))
                {
                    return false;
                }

                sceneHandle = slot.Handle;
                return sceneHandle != null;
            }

            public bool TryGetAnySceneHandle(string location, out YooAsset.SceneHandle sceneHandle)
            {
                sceneHandle = null;
                if (string.Equals(_loadingMainSceneName, location, StringComparison.Ordinal))
                {
                    sceneHandle = _loadingMainSceneHandle;
                    return sceneHandle != null;
                }

                if (string.Equals(CurrentMainSceneName, location, StringComparison.Ordinal))
                {
                    sceneHandle = CurrentMainSceneHandle;
                    return sceneHandle != null;
                }

                return TryGetSubScene(location, out sceneHandle);
            }

            public void RemoveSubScene(string location)
            {
                _subScenes.Remove(location);
            }

            public bool IsContainScene(string location)
            {
                return !string.IsNullOrEmpty(location) &&
                       (string.Equals(CurrentMainSceneName, location, StringComparison.Ordinal) ||
                        string.Equals(_loadingMainSceneName, location, StringComparison.Ordinal) ||
                        _subScenes.ContainsKey(location));
            }

            public bool IsMainScene(string location)
            {
                return !string.IsNullOrEmpty(location) && string.Equals(CurrentMainSceneName, location, StringComparison.Ordinal);
            }

            private sealed class SceneSlot
            {
                public SceneSlot(YooAsset.SceneHandle handle, SceneLoadState state)
                {
                    Handle = handle;
                    State = state;
                }

                public YooAsset.SceneHandle Handle { get; }

                public SceneLoadState State { get; set; }
            }

            private enum SceneLoadState
            {
                Loading,
                Loaded,
                Unloading
            }
        }
    }
}
