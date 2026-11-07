using System;
using System.Collections;
using System.Threading;
using AlicizaX.Resource.Runtime;
using AlicizaX.Scene.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Scene.Tests
{
    using Scene = UnityEngine.SceneManagement.Scene;

    public sealed class SceneServicePlayModeTests
    {
        private const string PackageName = "DefaultPackage";
        private const string SceneA = "TestSceneA";
        private const string SceneB = "TestSceneB";
        private const string SceneC = "TestSceneC";
        private const int FrameBudget = 900;

        private static GameObject s_controller;

        private ServiceRegistry _registry;
        private ResourceService _resources;
        private ISceneService _service;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PinTestRunner();

            _registry = new ServiceRegistry();
            _resources = new ResourceService
            {
                PlayMode = EPlayMode.EditorSimulateMode,
                DefaultPackageName = PackageName,
            };
            _registry.Register<IResourceService, IResourcePackageService>(_resources);
            _resources.Initialize();

            ResourcePackage package = YooAssets.GetPackage(PackageName);
            if (package.InitializeStatus != EOperationStatus.Succeeded)
            {
                PackageBuildResult build =
                    EditorSimulateBuildInvoker.Build(PackageName, (int)EBundleType.VirtualAssetBundle);
                InitializePackageOperation operation = package.InitializePackageAsync(new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters =
                        FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory),
                });

                yield return operation;
                Assert.That(operation.Status, Is.EqualTo(EOperationStatus.Succeeded),
                    "package init failed: " + operation.Error);
            }

            RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync();
            yield return versionOperation;
            Assert.That(versionOperation.Status, Is.EqualTo(EOperationStatus.Succeeded),
                "request package version failed: " + versionOperation.Error);

            LoadPackageManifestOperation manifestOperation =
                package.LoadPackageManifestAsync(new LoadPackageManifestOptions(versionOperation.PackageVersion, 60));
            yield return manifestOperation;
            Assert.That(manifestOperation.Status, Is.EqualTo(EOperationStatus.Succeeded),
                "load package manifest failed: " + manifestOperation.Error);

            _service = _registry.Register<ISceneService>(new SceneService());
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _service = null;
            _registry?.Dispose();
            _registry = null;
            for (int frame = 0; frame < 10; frame++)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator MainScene_load_is_idempotent()
        {
            Scene first = default;
            yield return _service.LoadMainAsync(SceneA).ToCoroutine(value => first = value);

            Assert.That(first.IsValid() && first.isLoaded, Is.True, "主场景应加载完成");
            Assert.That(first.name, Is.EqualTo(SceneA));
            Assert.That(_service.MainScene.name, Is.EqualTo(SceneA));
            Assert.That(_service.Contains(SceneA), Is.True);

            int count = SceneManager.sceneCount;
            Scene second = default;
            yield return _service.LoadMainAsync(SceneA).ToCoroutine(value => second = value);

            Assert.That(second.handle, Is.EqualTo(first.handle), "重复加载必须返回同一个场景实例");
            Assert.That(SceneManager.sceneCount, Is.EqualTo(count), "重复加载不得新建场景");
        }

        [UnityTest]
        public IEnumerator ReloadMainScene_replaces_the_scene()
        {
            Scene first = default;
            yield return _service.LoadMainAsync(SceneA).ToCoroutine(value => first = value);

            Scene reloaded = default;
            yield return _service.ReloadMainAsync(SceneA).ToCoroutine(value => reloaded = value);

            Assert.That(reloaded.IsValid() && reloaded.isLoaded, Is.True);
            Assert.That(reloaded.handle, Is.Not.EqualTo(first.handle), "重载必须产生新的场景实例");
            Assert.That(first.IsValid(), Is.False, "旧场景必须被卸载");
            Assert.That(_service.Contains(SceneA), Is.True);
        }

        [UnityTest]
        public IEnumerator Additive_load_unload_keeps_scene_set_consistent()
        {
            yield return _service.LoadMainAsync(SceneA).ToCoroutine();
            int baseline = SceneManager.sceneCount;

            yield return _service.LoadAdditiveAsync(SceneB).ToCoroutine(value => Assert.That(value.name, Is.EqualTo(SceneB)));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(baseline + 1));
            Assert.That(_service.Contains(SceneB), Is.True);
            Assert.That(_service.MainScene.name, Is.EqualTo(SceneA), "附加场景不得顶替主场景");

            yield return _service.LoadAdditiveAsync(SceneB).ToCoroutine();
            Assert.That(SceneManager.sceneCount, Is.EqualTo(baseline + 1), "重复加载附加场景不得新建场景");

            yield return _service.UnloadAdditiveAsync(SceneB).ToCoroutine();
            yield return null;

            Assert.That(_service.Contains(SceneB), Is.False);
            Assert.That(SceneManager.sceneCount, Is.EqualTo(baseline));
        }

        [UnityTest]
        public IEnumerator Suspended_additive_load_waits_for_activation()
        {
            yield return _service.LoadMainAsync(SceneA).ToCoroutine();

            float progress = 0f;
            UniTask<Scene> task = _service.LoadAdditiveAsync(SceneB, Progress.Create<float>(value => progress = value),
                suspend: true);
            var awaiter = task.GetAwaiter();

            for (int frame = 0; frame < FrameBudget && !awaiter.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(awaiter.IsCompleted, Is.False, "挂起加载在放行前不得完成");
            Assert.That(progress, Is.GreaterThan(0.5f), "挂起期间应已上报接近激活点的进度");
            Assert.That(_service.Contains(SceneB), Is.True, "挂起中的场景应可被查询到");

            Assert.That(_service.AllowActivation(SceneB), Is.True, "AllowActivation 应命中挂起中的场景");
            for (int frame = 0; frame < FrameBudget && !awaiter.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(awaiter.IsCompleted, Is.True, "放行后加载应完成");
            Assert.That(awaiter.GetResult().isLoaded, Is.True);
        }

        [UnityTest]
        public IEnumerator PreCancelled_load_leaves_no_scene_behind()
        {
            yield return _service.LoadMainAsync(SceneA).ToCoroutine();
            int baseline = SceneManager.sceneCount;

            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            UniTask<Scene> task = _service.LoadAdditiveAsync(SceneB, cancellationToken: cancellation.Token);
            var awaiter = task.GetAwaiter();
            for (int frame = 0; frame < 30; frame++)
            {
                yield return null;
            }

            Assert.That(awaiter.IsCompleted, Is.True, "已取消的加载必须结束");
            bool cancelled = false;
            try
            {
                awaiter.GetResult();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.That(cancelled, Is.True, "取消必须以 OperationCanceledException 结束");
            Assert.That(_service.Contains(SceneB), Is.False, "取消后不得残留登记");

            for (int frame = 0; frame < FrameBudget; frame++)
            {
                if (SceneManager.sceneCount <= baseline) break;
                yield return null;
            }

            Assert.That(SceneManager.sceneCount, Is.EqualTo(baseline), "取消后不得残留场景");
            cancellation.Dispose();
        }

        [UnityTest]
        public IEnumerator Main_switch_discards_previous_scenes()
        {
            yield return _service.LoadMainAsync(SceneA).ToCoroutine();
            yield return _service.LoadAdditiveAsync(SceneC).ToCoroutine();
            Assert.That(_service.Contains(SceneC), Is.True);

            Scene switched = default;
            yield return _service.LoadMainAsync(SceneB).ToCoroutine(value => switched = value);

            Assert.That(switched.name, Is.EqualTo(SceneB));
            Assert.That(_service.MainScene.name, Is.EqualTo(SceneB));
            Assert.That(_service.Contains(SceneA), Is.False, "旧主场景必须从登记中移除");
            Assert.That(_service.Contains(SceneC), Is.False, "Single 已由 Unity 卸载附加场景，登记必须同步清空");
        }

        private static void PinTestRunner()
        {
            if (s_controller == null)
            {
                s_controller = GameObject.Find("Code-based tests runner");
            }

            if (s_controller != null && s_controller.scene.name != "DontDestroyOnLoad")
            {
                Object.DontDestroyOnLoad(s_controller);
            }
        }
    }
}
