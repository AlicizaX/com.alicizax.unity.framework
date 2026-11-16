#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using AlicizaX.Resource.Runtime;
using AlicizaX.Scene.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using YooAsset;
using Object = UnityEngine.Object;

namespace AlicizaX.Scene.Tests
{
    using Scene = UnityEngine.SceneManagement.Scene;

    public sealed class SceneTestAssetSetup : IPrebuildSetup, IPostBuildCleanup
    {
        internal const string Folder = "Assets/__FrameworkSceneTestScenes";
        internal static readonly string[] Names = { "SceneAuditA", "SceneAuditB", "SceneAuditC" };

        internal static string Location(int index) => Folder + "/" + Names[index] + ".unity";

        public void Setup()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));

            for (int i = 0; i < Names.Length; i++)
            {
                if (File.Exists(Location(i)))
                    continue;

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.MoveGameObjectToScene(new GameObject(Names[i] + "Marker"), scene);
                EditorSceneManager.SaveScene(scene, Location(i));
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.Refresh();
        }

        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(Folder);
        }
    }

    [PrebuildSetup(typeof(SceneTestAssetSetup))]
    [PostBuildCleanup(typeof(SceneTestAssetSetup))]
    public sealed class SceneServiceLoadTests
    {
        private sealed class FrameDriver : IPlayerLoopItem, IDisposable
        {
            private readonly ResourceService _service;
            private bool _stopped;

            internal FrameDriver(ResourceService service)
            {
                _service = service;
                PlayerLoopHelper.AddAction(PlayerLoopTiming.Update, this);
            }

            public bool MoveNext()
            {
                if (_stopped) return false;
                _service.ProcessFrame(Time.unscaledTime);
                return true;
            }

            public void Dispose() => _stopped = true;
        }

        private sealed class RecordingProgress : IProgress<float>
        {
            internal readonly List<float> Values = new List<float>();
            public void Report(float value) => Values.Add(value);
        }

        private static string A => SceneTestAssetSetup.Location(0);
        private static string B => SceneTestAssetSetup.Location(1);
        private static string C => SceneTestAssetSetup.Location(2);
        private const string Missing = "Assets/__FrameworkSceneTestScenes/Missing.unity";

        private ResourceService _resources;
        private SceneService _scenes;
        private ResourcePackage _package;
        private FrameDriver _driver;
        private string _packageName;
        private ScriptableObject _settings;
        private object _previousSettings;
        private FieldInfo _settingsField;
        private bool _initializedGlobal;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _packageName = "SceneAudit" + Guid.NewGuid().ToString("N");
            var editorAssembly = Assembly.Load("YooAsset.Editor");
            var settingsType = editorAssembly.GetType("YooAsset.Editor.BundleCollectorSetting", true);
            var dataType = editorAssembly.GetType("YooAsset.Editor.BundleCollectorSettingData", true);
            _settingsField = dataType.GetField("s_setting", BindingFlags.NonPublic | BindingFlags.Static);
            _previousSettings = _settingsField.GetValue(null);
            _settings = ScriptableObject.CreateInstance(settingsType);
            object collectorPackage = Activator.CreateInstance(editorAssembly.GetType("YooAsset.Editor.BundleCollectorPackage", true));
            collectorPackage.GetType().GetField("PackageName").SetValue(collectorPackage, _packageName);
            object group = Activator.CreateInstance(editorAssembly.GetType("YooAsset.Editor.BundleCollectorGroup", true));
            group.GetType().GetField("GroupName").SetValue(group, "Scenes");
            object collector = Activator.CreateInstance(editorAssembly.GetType("YooAsset.Editor.BundleCollector", true));
            collector.GetType().GetField("CollectPath").SetValue(collector, SceneTestAssetSetup.Folder);
            ((IList)group.GetType().GetField("Collectors").GetValue(group)).Add(collector);
            ((IList)collectorPackage.GetType().GetField("Groups").GetValue(collectorPackage)).Add(group);
            ((IList)settingsType.GetField("Packages").GetValue(_settings)).Add(collectorPackage);
            _settingsField.SetValue(null, _settings);

            AppServices.GetOrCreateRegistry();
            _resources = new ResourceService { DefaultPackageName = _packageName, PlayMode = EPlayMode.EditorSimulateMode };
            AppServices.Register<IResourceService, IResourcePackageService>(_resources);
            _driver = new FrameDriver(_resources);
            _initializedGlobal = !YooAssets.IsInitialized;
            _resources.Initialize();
            yield return Wait(_resources.InitPackageAsync(), success => Assert.That(success, Is.True));
            _package = YooAssets.GetPackage(_packageName);
            var version = _package.RequestPackageVersionAsync();
            yield return version;
            Assert.That(version.Status, Is.EqualTo(EOperationStatus.Succeeded), version.Error);
            var manifest = _resources.LoadPackageManifestAsync(version.PackageVersion);
            yield return manifest;
            Assert.That(manifest.Status, Is.EqualTo(EOperationStatus.Succeeded), manifest.Error);
            _scenes = AppServices.Register<ISceneService>(new SceneService()) as SceneService;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ActivateSuspendedLoads();
            Scene cleanup = SceneManager.CreateScene("SceneAuditCleanup" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            var unloads = new List<AsyncOperation>();
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != cleanup && scene.isLoaded)
                    unloads.Add(SceneManager.UnloadSceneAsync(scene));
            }

            foreach (AsyncOperation unload in unloads)
            {
                while (!unload.isDone) yield return null;
            }

            _driver?.Dispose();
            AppServices.Shutdown();
            if (_package != null)
            {
                var destroy = _package.DestroyPackageAsync();
                yield return destroy;
                Assert.That(destroy.Status, Is.EqualTo(EOperationStatus.Succeeded), destroy.Error);
                YooAssets.RemovePackage(_packageName);
            }

            if (_initializedGlobal && YooAssets.IsInitialized) YooAssets.Destroy();
            _settingsField.SetValue(null, _previousSettings);
            Object.DestroyImmediate(_settings);
        }

        [UnityTest]
        public IEnumerator LoadMainCommitsAndRepeatedLoadReturnsTheSameInstance()
        {
            var progress = new RecordingProgress();
            Scene first = default;
            yield return Wait(_scenes.LoadMainAsync(A, progress), scene => first = scene);
            Assert.That(first.name, Is.EqualTo(SceneTestAssetSetup.Names[0]));
            Assert.That(first.isLoaded, Is.True);
            Assert.That(first.GetRootGameObjects()[0].name, Is.EqualTo(SceneTestAssetSetup.Names[0] + "Marker"));
            Assert.That(_scenes.MainScene, Is.EqualTo(first));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(first));
            Assert.That(_scenes.Contains(A), Is.True);
            Assert.That(progress.Values[0], Is.Zero);
            Assert.That(progress.Values[progress.Values.Count - 1], Is.EqualTo(1f));
            for (int i = 1; i < progress.Values.Count; i++)
                Assert.That(progress.Values[i], Is.GreaterThanOrEqualTo(progress.Values[i - 1]));

            Scene again = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => again = scene);
            Assert.That(again, Is.EqualTo(first));
            Assert.Throws<GameFrameworkException>(() => _scenes.UnloadAdditiveAsync(A));
        }

        [UnityTest]
        public IEnumerator SuspendedReloadKeepsCommittedMainUntilActivation()
        {
            Scene first = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => first = scene);
            var reload = _scenes.ReloadMainAsync(A, suspend: true);
            yield return Frames(10);
            Assert.That(reload.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(_scenes.MainScene, Is.EqualTo(first), "pending reload must not replace the committed main scene");
            Assert.That(first.isLoaded, Is.True);
            Assert.That(_scenes.Contains(A), Is.True);
            yield return Fail<Scene, GameFrameworkException>(_scenes.ReloadMainAsync(A), null);
            Assert.That(_scenes.AllowActivation(A), Is.True, "AllowActivation targets the pending instance");
            Scene second = default;
            yield return Wait(reload, scene => second = scene);
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(first.isLoaded, Is.False);
            Assert.That(_scenes.MainScene, Is.EqualTo(second));
            Assert.That(_scenes.Contains(A), Is.True);
            Assert.That(_scenes.AllowActivation(A), Is.False);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(second));
        }

        [UnityTest]
        public IEnumerator FailedMainLoadKeepsTheCommittedMainScene()
        {
            LogAssert.ignoreFailingMessages = true;
            Scene first = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => first = scene);
            GameFrameworkException failure = null;
            yield return Fail<Scene, GameFrameworkException>(_scenes.LoadMainAsync(Missing), exception => failure = exception);
            StringAssert.Contains(Missing, failure.Message);
            Assert.That(_scenes.MainScene, Is.EqualTo(first));
            Assert.That(first.isLoaded, Is.True);
            Assert.That(_scenes.Contains(A), Is.True);
            Assert.That(_scenes.Contains(Missing), Is.False);
            Assert.Throws<GameFrameworkException>(() => _scenes.UnloadAdditiveAsync(A), "A is still the main scene");
            Scene again = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => again = scene);
            Assert.That(again, Is.EqualTo(first), "the committed main scene is still recognised");
            Scene next = default;
            yield return Wait(_scenes.LoadMainAsync(B), scene => next = scene);
            Assert.That(_scenes.MainScene, Is.EqualTo(next));
        }

        [UnityTest]
        public IEnumerator CancelledMainLoadStopsWaitingButStillCommits()
        {
            Scene first = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => first = scene);
            using var cts = new CancellationTokenSource();
            var load = _scenes.LoadMainAsync(B, cancellationToken: cts.Token);
            cts.Cancel();
            yield return Fail<Scene, OperationCanceledException>(load, null);
            yield return Until(() => _scenes.MainScene.name == SceneTestAssetSetup.Names[1]);
            Assert.That(first.isLoaded, Is.False);
            Assert.That(_scenes.Contains(B), Is.True);
            Assert.That(_scenes.Contains(A), Is.False);
            Assert.Throws<GameFrameworkException>(() => _scenes.UnloadAdditiveAsync(B));
        }

        [UnityTest]
        public IEnumerator CancelledSuspendedMainLoadStaysPendingUntilActivation()
        {
            Scene first = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => first = scene);
            using var cts = new CancellationTokenSource();
            var load = _scenes.LoadMainAsync(B, suspend: true, cancellationToken: cts.Token);
            yield return Frames(10);
            cts.Cancel();
            yield return Fail<Scene, OperationCanceledException>(load, null);
            Assert.That(_scenes.MainScene, Is.EqualTo(first));
            Assert.That(first.isLoaded, Is.True, "cancelling a Single load must not activate it");
            Assert.That(_scenes.Contains(B), Is.True);
            yield return Fail<Scene, GameFrameworkException>(_scenes.LoadMainAsync(C), null);
            yield return Fail<Scene, GameFrameworkException>(_scenes.LoadAdditiveAsync(C), null);
            Assert.That(_scenes.AllowActivation(B), Is.True);
            yield return Until(() => _scenes.MainScene.name == SceneTestAssetSetup.Names[1]);
            Assert.That(first.isLoaded, Is.False);
            Assert.That(_scenes.Contains(A), Is.False);
            Scene next = default;
            yield return Wait(_scenes.LoadMainAsync(C), scene => next = scene);
            Assert.That(_scenes.MainScene, Is.EqualTo(next));
        }

        [UnityTest]
        public IEnumerator AdditiveUnloadKeepsTheEntryUntilTheOperationCompletes()
        {
            yield return Wait(_scenes.LoadMainAsync(A), _ => { });
            Scene additive = default;
            yield return Wait(_scenes.LoadAdditiveAsync(B), scene => additive = scene);
            Assert.That(additive.isLoaded, Is.True);
            Assert.That(_scenes.Contains(B), Is.True);
            Scene same = default;
            yield return Wait(_scenes.LoadAdditiveAsync(B), scene => same = scene);
            Assert.That(same, Is.EqualTo(additive));

            UniTask unload = _scenes.UnloadAdditiveAsync(B);
            Assert.That(unload.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(_scenes.Contains(B), Is.True);
            Assert.Throws<GameFrameworkException>(() => _scenes.UnloadAdditiveAsync(B));
            yield return Fail<Scene, GameFrameworkException>(_scenes.LoadAdditiveAsync(B), null);
            yield return Fail<Scene, GameFrameworkException>(_scenes.LoadMainAsync(B), null);
            yield return Wait(unload);
            Assert.That(additive.isLoaded, Is.False);
            Assert.That(_scenes.Contains(B), Is.False);

            Scene reloaded = default;
            yield return Wait(_scenes.LoadAdditiveAsync(B), scene => reloaded = scene);
            Assert.That(reloaded.isLoaded, Is.True);
            Assert.That(reloaded, Is.Not.EqualTo(additive));
        }

        [UnityTest]
        public IEnumerator CancelledUnloadWaitStillCompletesTheUnload()
        {
            yield return Wait(_scenes.LoadMainAsync(A), _ => { });
            Scene additive = default;
            yield return Wait(_scenes.LoadAdditiveAsync(B), scene => additive = scene);
            using var cts = new CancellationTokenSource();
            UniTask unload = _scenes.UnloadAdditiveAsync(B, cts.Token);
            cts.Cancel();
            yield return Fail<OperationCanceledException>(unload);
            Assert.Throws<GameFrameworkException>(() => _scenes.UnloadAdditiveAsync(B), "the unload is still in flight");
            yield return Until(() => !_scenes.Contains(B));
            Assert.That(additive.isLoaded, Is.False);
            yield return Wait(_scenes.LoadAdditiveAsync(B), _ => { });
            Assert.That(_scenes.Contains(B), Is.True);
        }

        [UnityTest]
        public IEnumerator CancelledAdditiveLoadRollsBack()
        {
            yield return Wait(_scenes.LoadMainAsync(A), _ => { });
            using var cts = new CancellationTokenSource();
            var load = _scenes.LoadAdditiveAsync(B, suspend: true, cancellationToken: cts.Token);
            yield return Frames(10);
            cts.Cancel();
            yield return Fail<Scene, OperationCanceledException>(load, null);
            Assert.That(_scenes.Contains(B), Is.False);
            yield return Until(() => !SceneManager.GetSceneByPath(B).isLoaded);
            yield return Frames(5);
            Assert.That(SceneManager.GetSceneByPath(B).isLoaded, Is.False);
            yield return Wait(_scenes.LoadAdditiveAsync(B), _ => { });
            Assert.That(_scenes.Contains(B), Is.True);
        }

        [UnityTest]
        public IEnumerator LoadingAnAdditiveLocationAsMainPromotesIt()
        {
            Scene first = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => first = scene);
            yield return Wait(_scenes.LoadAdditiveAsync(B), _ => { });
            Scene main = default;
            yield return Wait(_scenes.LoadMainAsync(B), scene => main = scene);
            Assert.That(main.name, Is.EqualTo(SceneTestAssetSetup.Names[1]));
            Assert.That(_scenes.MainScene, Is.EqualTo(main));
            Assert.That(first.isLoaded, Is.False);
            Assert.That(_scenes.Contains(A), Is.False);
            Assert.Throws<GameFrameworkException>(() => _scenes.UnloadAdditiveAsync(B), "B is the main scene now");
        }

        [UnityTest]
        public IEnumerator DisposingWithAPendingMainLoadNeverCommitsIntoTheDisposedService()
        {
            yield return Wait(_scenes.LoadMainAsync(A), _ => { });
            var load = _scenes.LoadMainAsync(B, suspend: true);
            yield return Frames(10);
            var pending = (SceneHandle)typeof(SceneService).GetField("_pendingMainHandle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_scenes);
            Assert.That(pending, Is.Not.Null);
            Assert.That(AppServices.Unregister(_scenes), Is.True);
            yield return Fail<Scene, OperationCanceledException>(load, null);
            Assert.That(_scenes.Contains(B), Is.False);
            Assert.That(pending.AllowSceneActivation(), Is.True);
            yield return Until(() => pending.IsDone);
            Assert.That(_scenes.Contains(B), Is.False);
            Assert.That(_scenes.Contains(A), Is.False);
            Assert.Throws<ObjectDisposedException>(() => _scenes.LoadMainAsync(C).GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator SetActiveSwitchesCommittedScenesWhileAMainTransitionIsSuspended()
        {
            Scene first = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => first = scene);
            Scene additive = default;
            yield return Wait(_scenes.LoadAdditiveAsync(B), scene => additive = scene);
            var reload = _scenes.ReloadMainAsync(A, suspend: true);
            yield return Frames(10);
            Assert.That(_scenes.SetActive(B), Is.True);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(additive));
            Assert.That(_scenes.SetActive(A), Is.True, "SetActive targets the committed instance, not the suspended one");
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(first));
            Assert.That(_scenes.AllowActivation(A), Is.True);
            Scene second = default;
            yield return Wait(reload, scene => second = scene);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(second));
            Assert.That(additive.isLoaded, Is.False);
            Assert.That(_scenes.Contains(B), Is.False);
        }

        [UnityTest]
        public IEnumerator DisposeUnloadsSettledAdditiveScenesAndLeavesTheWorldLoaded()
        {
            Scene main = default;
            yield return Wait(_scenes.LoadMainAsync(A), scene => main = scene);
            Scene settled = default;
            yield return Wait(_scenes.LoadAdditiveAsync(B), scene => settled = scene);
            var inFlight = _scenes.LoadAdditiveAsync(C, suspend: true);
            yield return Frames(10);
            var problems = new List<string>();
            Application.LogCallback capture = (message, _, type) => { if (type != LogType.Log) problems.Add(type + ": " + message); };
            Application.logMessageReceived += capture;
            try
            {
                Assert.That(AppServices.Unregister(_scenes), Is.True);
                yield return Fail<Scene, OperationCanceledException>(inFlight, null);
                yield return Until(() => !settled.isLoaded && !SceneManager.GetSceneByPath(C).isLoaded);
                yield return Frames(5);
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(main.isLoaded, Is.True, "the main scene is the world and outlives the service");
            Assert.That(settled.isLoaded, Is.False);
            Assert.That(SceneManager.GetSceneByPath(C).isLoaded, Is.False, "the cancelled in-flight load rolls itself back");
            Assert.That(_scenes.Contains(A) || _scenes.Contains(B) || _scenes.Contains(C), Is.False);
        }

        [UnityTest]
        public IEnumerator DisposeLeavesAnInFlightUnloadToItsOwnOperation()
        {
            yield return Wait(_scenes.LoadMainAsync(A), _ => { });
            Scene additive = default;
            yield return Wait(_scenes.LoadAdditiveAsync(B), scene => additive = scene);
            UniTask unload = _scenes.UnloadAdditiveAsync(B);
            var problems = new List<string>();
            Application.LogCallback capture = (message, _, type) => { if (type != LogType.Log) problems.Add(type + ": " + message); };
            Application.logMessageReceived += capture;
            try
            {
                Assert.That(AppServices.Unregister(_scenes), Is.True);
                yield return Fail<OperationCanceledException>(unload);
                yield return Until(() => !additive.isLoaded);
                yield return Frames(5);
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(additive.isLoaded, Is.False);
        }

        private void ActivateSuspendedLoads()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            ((SceneHandle)typeof(SceneService).GetField("_pendingMainHandle", fields).GetValue(_scenes))?.AllowSceneActivation();
            foreach (SceneHandle handle in ((Dictionary<string, SceneHandle>)typeof(SceneService).GetField("_scenes", fields).GetValue(_scenes)).Values)
            {
                if (handle.IsValid && !handle.IsDone)
                    handle.AllowSceneActivation();
            }
        }

        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator Until(Func<bool> condition)
        {
            for (int frame = 0; !condition() && frame < 600; frame++) yield return null;
            Assert.That(condition(), Is.True, "condition not reached within 600 frames");
        }

        private static IEnumerator Wait<T>(UniTask<T> task, Action<T> result)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 600; frame++) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "operation did not finish within 600 frames");
            result(awaiter.GetResult());
        }

        private static IEnumerator Wait(UniTask task)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 600; frame++) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "operation did not finish within 600 frames");
            awaiter.GetResult();
        }

        private static IEnumerator Fail<T, TException>(UniTask<T> task, Action<TException> inspect) where TException : Exception
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 600; frame++) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "operation did not finish within 600 frames");
            TException caught = null;
            try { awaiter.GetResult(); }
            catch (TException exception) { caught = exception; }
            Assert.That(caught, Is.Not.Null, "expected " + typeof(TException).Name);
            inspect?.Invoke(caught);
        }

        private static IEnumerator Fail<TException>(UniTask task) where TException : Exception
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 600; frame++) yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "operation did not finish within 600 frames");
            TException caught = null;
            try { awaiter.GetResult(); }
            catch (TException exception) { caught = exception; }
            Assert.That(caught, Is.Not.Null, "expected " + typeof(TException).Name);
        }
    }
}
#endif
