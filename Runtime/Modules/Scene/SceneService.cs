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

        private CancellationTokenSource _operations;
        private IResourcePackageService _packageService;
        private IResourceService _resourceService;
        private string _mainLocation = string.Empty;

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

        public bool Contains(string location) => TryGetHandle(location, out _);

        public bool SetActive(string location)
            => TryGetHandle(location, out SceneHandle handle) && handle.ActivateScene();

        public bool AllowActivation(string location)
            => TryGetHandle(location, out SceneHandle handle) && !handle.IsDone && handle.AllowSceneActivation();

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

            foreach (var pair in _scenes)
            {
                ReleaseScene(pair.Value);
            }

            _scenes.Clear();
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

            if (string.Equals(_mainLocation, location, StringComparison.Ordinal))
            {
                throw new GameFrameworkException($"Unloading the main scene is not supported. Location: {location}");
            }

            if (!TryGetHandle(location, out SceneHandle handle) || !handle.IsDone)
            {
                throw new GameFrameworkException($"Additive scene is not loaded: {location}");
            }

            return UnloadAdditiveSceneAsync(location, handle, cancellationToken);
        }

        private async UniTask<Scene> LoadMainSceneAsync(string location, IProgress<float> progress, bool suspend,
            CancellationToken cancellationToken, bool reload)
        {
            bool tracked = TryGetHandle(location, out SceneHandle existing);

            if (!reload && tracked && IsLoaded(existing))
            {
                progress?.Report(1f);
                return existing.SceneObject;
            }

            if (tracked && !existing.IsDone)
            {
                throw new GameFrameworkException($"Scene is already loading or suspended: {location}");
            }

            if (IsMainLoading)
            {
                throw new GameFrameworkException($"Main scene transition already in flight. Rejected: {location}");
            }

            SceneHandle handle = null;
            try
            {
                progress?.Report(0f);

                CancellationTokenSource previous = Operations;
                ResourcePackage package = YooAssets.GetPackage(_packageService.DefaultPackageName);

                previous.Cancel();
                previous.Dispose();
                _operations = new CancellationTokenSource();

                using CancellationTokenSource linked = CreateLinkedToken(cancellationToken);
                CancellationToken token = linked != null ? linked.Token : Operations.Token;

                handle = package.LoadSceneAsync(location, LoadSceneMode.Single, LocalPhysicsMode.None, !suspend, ScenePriority);
                _scenes[location] = handle;
                _mainLocation = location;

                await handle.ToUniTask(progress, cancellationToken: token);

                if (!IsLoaded(handle))
                {
                    throw new GameFrameworkException(
                        $"Load main scene failed. Location: {location}, Error: {handle.Error}");
                }

                _scenes.Clear();
                _scenes[location] = handle;

                _resourceService.ForceUnloadUnusedAssets(false);
                progress?.Report(1f);
                return handle.SceneObject;
            }
            catch
            {
                if (handle != null)
                {
                    _scenes.Remove(location);
                    _mainLocation = string.Empty;
                    ReleaseScene(handle);
                }

                throw;
            }
        }

        private async UniTask<Scene> LoadAdditiveSceneAsync(string location, IProgress<float> progress, bool suspend,
            CancellationToken cancellationToken)
        {
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

            if (IsMainLoading)
            {
                throw new GameFrameworkException($"Main scene transition already in flight. Rejected: {location}");
            }

            SceneHandle handle = null;
            try
            {
                progress?.Report(0f);

                using CancellationTokenSource linked = CreateLinkedToken(cancellationToken);
                CancellationToken token = linked != null ? linked.Token : Operations.Token;

                ResourcePackage package = YooAssets.GetPackage(_packageService.DefaultPackageName);
                handle = package.LoadSceneAsync(location, LoadSceneMode.Additive, LocalPhysicsMode.None, !suspend, ScenePriority);
                _scenes[location] = handle;

                await handle.ToUniTask(progress, cancellationToken: token);

                if (!IsLoaded(handle))
                {
                    throw new GameFrameworkException(
                        $"Load additive scene failed. Location: {location}, Error: {handle.Error}");
                }

                progress?.Report(1f);
                return handle.SceneObject;
            }
            catch
            {
                _scenes.Remove(location);
                ReleaseScene(handle);
                throw;
            }
        }

        private async UniTask UnloadAdditiveSceneAsync(string location, SceneHandle handle,
            CancellationToken cancellationToken)
        {
            _scenes.Remove(location);

            using CancellationTokenSource linked = CreateLinkedToken(cancellationToken);
            CancellationToken token = linked != null ? linked.Token : Operations.Token;

            UnloadSceneOperation operation = handle.UnloadSceneAsync();
            await operation.ToUniTask(cancellationToken: token);

            if (operation.Status != EOperationStatus.Succeeded)
            {
                if (IsLoaded(handle))
                {
                    _scenes[location] = handle;
                }

                throw new GameFrameworkException(
                    $"Unload additive scene failed. Location: {location}, Error: {operation.Error}");
            }
        }

        private CancellationTokenSource CreateLinkedToken(CancellationToken cancellationToken)
            => cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(Operations.Token, cancellationToken)
                : null;

        private bool IsMainLoading => TryGetHandle(_mainLocation, out SceneHandle handle) && !handle.IsDone;

        private CancellationTokenSource Operations
            => _operations ?? throw new ObjectDisposedException(nameof(SceneService));

        private bool TryGetHandle(string location, out SceneHandle handle)
        {
            handle = null;
            return !string.IsNullOrEmpty(location) && _scenes.TryGetValue(location, out handle) && handle.IsValid;
        }

        private static bool IsLoaded(SceneHandle handle)
        {
            if (!handle.IsValid || handle.Status != EOperationStatus.Succeeded)
            {
                return false;
            }

            return handle.SceneObject.isLoaded;
        }

        private static void ReleaseScene(SceneHandle handle)
        {
            if (handle is not { IsValid: true })
            {
                return;
            }

            if (handle.IsDone && !handle.SceneObject.isLoaded)
            {
                handle.Release();
                return;
            }

            handle.UnloadSceneAsync();
        }
    }
}
