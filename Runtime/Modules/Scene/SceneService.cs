using System;
using System.Collections.Generic;
using System.Threading;
using AlicizaX.Resource.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using YooAsset;
using SceneHandle = YooAsset.SceneHandle;

namespace AlicizaX.Scene.Runtime
{
    using Scene = UnityEngine.SceneManagement.Scene;

    internal sealed class SceneService : ServiceBase, ISceneService
    {
        private const uint ScenePriority = 100;

        private readonly Dictionary<string, SceneHandle> _scenes = new Dictionary<string, SceneHandle>(StringComparer.Ordinal);
        private readonly HashSet<string> _unloadingLocations = new HashSet<string>(StringComparer.Ordinal);

        private CancellationTokenSource _operations;
        private IResourcePackageService _packageService;
        private IResourceService _resourceService;
        private string _mainLocation = string.Empty;
        private SceneHandle _pendingMainHandle;
        private string _pendingMainLocation;

        public Scene MainScene
        {
            get
            {
                if (TryGetHandle(_mainLocation, out SceneHandle handle) && IsLoaded(handle))
                {
                    return handle.SceneObject;
                }

                return SceneManager.GetActiveScene();
            }
        }

        public bool Contains(string location) => IsPendingMain(location) || TryGetHandle(location, out _);

        public bool SetActive(string location)
            => TryGetHandle(location, out SceneHandle handle) && handle.ActivateScene();

        public bool AllowActivation(string location)
        {
            if (IsPendingMain(location))
            {
                return _pendingMainHandle.AllowSceneActivation();
            }

            return TryGetHandle(location, out SceneHandle handle) && !handle.IsDone && handle.AllowSceneActivation();
        }

        protected override void OnInitialize()
        {
            _packageService = Require<IResourcePackageService>();
            _resourceService = Require<IResourceService>();
            _operations = new CancellationTokenSource();
        }

        protected override void OnDispose()
        {
            _operations.Cancel();
            _operations.Dispose();
            _operations = null;
            _pendingMainHandle = null;
            _pendingMainLocation = null;

            foreach (var pair in _scenes)
            {
                if (pair.Value.IsValid && pair.Value.IsDone && !_unloadingLocations.Contains(pair.Key) && !IsMain(pair.Key))
                {
                    ReleaseScene(pair.Value);
                }
            }

            _scenes.Clear();
            _unloadingLocations.Clear();
            _mainLocation = string.Empty;
        }

        public UniTask<Scene> LoadMainAsync(string location, IProgress<float> progress = null, bool suspend = false,
            CancellationToken cancellationToken = default)
        {
            GameFrameworkGuard.NotNullOrEmpty(location, nameof(location));
            return LoadMainSceneAsync(location, progress, suspend, cancellationToken, reload: false);
        }

        public UniTask<Scene> ReloadMainAsync(string location, IProgress<float> progress = null, bool suspend = false,
            CancellationToken cancellationToken = default)
        {
            GameFrameworkGuard.NotNullOrEmpty(location, nameof(location));
            return LoadMainSceneAsync(location, progress, suspend, cancellationToken, reload: true);
        }

        public UniTask<Scene> LoadAdditiveAsync(string location, IProgress<float> progress = null, bool suspend = false,
            CancellationToken cancellationToken = default)
        {
            GameFrameworkGuard.NotNullOrEmpty(location, nameof(location));
            return LoadAdditiveSceneAsync(location, progress, suspend, cancellationToken);
        }

        public UniTask UnloadAdditiveAsync(string location, CancellationToken cancellationToken = default)
        {
            GameFrameworkGuard.NotNullOrEmpty(location, nameof(location));

            if (IsMain(location))
            {
                throw new GameFrameworkException($"Unloading the main scene is not supported. Location: {location}");
            }

            if (_unloadingLocations.Contains(location))
            {
                throw new GameFrameworkException($"Additive scene is already unloading: {location}");
            }

            if (!TryGetHandle(location, out SceneHandle handle) || !handle.IsDone)
            {
                throw new GameFrameworkException($"Additive scene is not loaded: {location}");
            }

            _unloadingLocations.Add(location);
            return UnloadAdditiveSceneAsync(location, handle, Operations.Token).AttachExternalCancellation(cancellationToken);
        }

        private async UniTask<Scene> LoadMainSceneAsync(string location, IProgress<float> progress, bool suspend,
            CancellationToken cancellationToken, bool reload)
        {
            if (IsMainLoading)
            {
                throw new GameFrameworkException($"Main scene transition already in flight. Rejected: {location}");
            }

            if (_unloadingLocations.Contains(location))
            {
                throw new GameFrameworkException($"Scene is unloading: {location}");
            }

            bool tracked = TryGetHandle(location, out SceneHandle existing);
            if (!reload && tracked && IsMain(location) && IsLoaded(existing))
            {
                progress?.Report(1f);
                return existing.SceneObject;
            }

            if (tracked && !existing.IsDone)
            {
                throw new GameFrameworkException($"Scene is already loading or suspended: {location}");
            }

            progress?.Report(0f);
            CancellationTokenSource previous = Operations;
            ResourcePackage package = YooAssets.GetPackage(_packageService.DefaultPackageName);
            previous.Cancel();
            previous.Dispose();
            _operations = new CancellationTokenSource();

            SceneHandle handle = package.LoadSceneAsync(location, LoadSceneMode.Single, LocalPhysicsMode.None, !suspend, ScenePriority);
            _pendingMainHandle = handle;
            _pendingMainLocation = location;

            Scene scene = await CompleteMainSceneAsync(location, handle, progress, _operations.Token)
                .AttachExternalCancellation(cancellationToken);
            progress?.Report(1f);
            return scene;
        }

        private async UniTask<Scene> CompleteMainSceneAsync(string location, SceneHandle handle, IProgress<float> progress,
            CancellationToken lifetimeToken)
        {
            await handle.ToUniTask(progress, cancellationToken: lifetimeToken);
            _pendingMainHandle = null;
            _pendingMainLocation = null;

            if (!IsLoaded(handle))
            {
                string error = handle.Error;
                ReleaseScene(handle);
                throw new GameFrameworkException($"Load main scene failed. Location: {location}, Error: {error}");
            }

            _scenes.Clear();
            _scenes[location] = handle;
            _mainLocation = location;
            _resourceService.ForceUnloadUnusedAssets(false);
            return handle.SceneObject;
        }

        private async UniTask<Scene> LoadAdditiveSceneAsync(string location, IProgress<float> progress, bool suspend,
            CancellationToken cancellationToken)
        {
            if (IsMainLoading)
            {
                throw new GameFrameworkException($"Main scene transition already in flight. Rejected: {location}");
            }

            if (_unloadingLocations.Contains(location))
            {
                throw new GameFrameworkException($"Scene is unloading: {location}");
            }

            bool tracked = TryGetHandle(location, out SceneHandle existing);
            if (tracked && IsLoaded(existing))
            {
                progress?.Report(1f);
                return existing.SceneObject;
            }

            if (tracked && !existing.IsDone)
            {
                throw new GameFrameworkException($"Scene is already loading or suspended: {location}");
            }

            progress?.Report(0f);
            using CancellationTokenSource linked = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(Operations.Token, cancellationToken)
                : null;
            CancellationToken token = linked != null ? linked.Token : Operations.Token;
            ResourcePackage package = YooAssets.GetPackage(_packageService.DefaultPackageName);
            SceneHandle handle = package.LoadSceneAsync(location, LoadSceneMode.Additive, LocalPhysicsMode.None, !suspend, ScenePriority);
            _scenes[location] = handle;

            try
            {
                await handle.ToUniTask(progress, cancellationToken: token);
                if (!IsLoaded(handle))
                {
                    throw new GameFrameworkException(
                        $"Load additive scene failed. Location: {location}, Error: {handle.Error}");
                }
            }
            catch
            {
                _scenes.Remove(location);
                if (handle.IsValid)
                {
                    ReleaseScene(handle);
                }

                throw;
            }

            progress?.Report(1f);
            return handle.SceneObject;
        }

        private async UniTask UnloadAdditiveSceneAsync(string location, SceneHandle handle, CancellationToken lifetimeToken)
        {
            UnloadSceneOperation operation = handle.UnloadSceneAsync();
            try
            {
                await operation.ToUniTask(cancellationToken: lifetimeToken);
            }
            finally
            {
                _unloadingLocations.Remove(location);
            }

            if (operation.Status != EOperationStatus.Succeeded)
            {
                throw new GameFrameworkException(
                    $"Unload additive scene failed. Location: {location}, Error: {operation.Error}");
            }

            _scenes.Remove(location);
        }

        private bool IsMainLoading => _pendingMainHandle != null;

        private bool IsMain(string location) => string.Equals(_mainLocation, location, StringComparison.Ordinal);

        private bool IsPendingMain(string location)
            => _pendingMainHandle != null && string.Equals(_pendingMainLocation, location, StringComparison.Ordinal);

        private CancellationTokenSource Operations
            => _operations ?? throw new ObjectDisposedException(nameof(SceneService));

        private bool TryGetHandle(string location, out SceneHandle handle)
        {
            handle = null;
            return !string.IsNullOrEmpty(location) && _scenes.TryGetValue(location, out handle) && handle.IsValid;
        }

        private static bool IsLoaded(SceneHandle handle)
            => handle.Status == EOperationStatus.Succeeded && handle.SceneObject.isLoaded;

        private static void ReleaseScene(SceneHandle handle)
        {
            if (handle.IsDone && !handle.SceneObject.isLoaded)
            {
                handle.Release();
                return;
            }

            handle.UnloadSceneAsync();
        }
    }
}
