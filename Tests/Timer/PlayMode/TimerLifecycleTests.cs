using System;
using System.Collections;
using System.Reflection;
using AlicizaX.Timer.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AlicizaX.Timer.Tests
{
    public sealed class TimerLifecycleTests
    {
        private GameObject rootObject, componentObject;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (componentObject != null) Object.Destroy(componentObject);
            if (rootObject != null) Object.Destroy(rootObject);
            yield return null;
            if (AppServices.HasWorld) AppServices.Shutdown();
        }

        private TimerTestRoot CreateRoot(bool persistent = true)
        {
            Assert.That(AppServices.HasWorld, Is.False, "test requires no pre-existing application world");
            rootObject = new GameObject("Timer test root");
            rootObject.SetActive(false);
            var root = rootObject.AddComponent<TimerTestRoot>();
            typeof(AppServiceRoot).GetField("_dontDestroyOnLoad", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(root, persistent);
            rootObject.AddComponent<TimerComponent>();
            rootObject.SetActive(true);
            return root;
        }

        [UnityTest]
        public IEnumerator RootDrivesTimerAndDuplicateComponentDoesNotReplaceService()
        {
            CreateRoot();
            var service = (TimerService)AppServices.App.Require<ITimerService>();
            Assert.That(typeof(AppServiceRoot).GetCustomAttribute<DefaultExecutionOrder>().order, Is.EqualTo(-32000));
            Assert.That(typeof(TimerComponent).GetCustomAttribute<DefaultExecutionOrder>().order, Is.EqualTo(-800));
            componentObject = new GameObject("duplicate timer bootstrap");
            componentObject.AddComponent<TimerComponent>();
            Assert.That(AppServices.App.Require<ITimerService>(), Is.SameAs(service));
            int calls = 0;
            service.AddTimer(() => calls++, 0.01f, isUnscaled: true);
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (calls == 0 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(calls, Is.EqualTo(1));
            TimerProbe.Statistics(service, 0, 0, capacity: 1024);
            TimerProbe.Ledger(service);
        }

        [UnityTest]
        public IEnumerator DisablingRootStopsDispatchAndResumeCatchesUpWithinTickBudget()
        {
            var root = CreateRoot();
            var service = (TimerService)AppServices.App.Require<ITimerService>();
            root.enabled = false;
            int calls = 0;
            service.AddTimer(() => calls++, 0.3f, isUnscaled: true);
            service.AddTimer(TimerProbe.NoOp, 60, isUnscaled: true);
            long before = TimerProbe.Cursor(service);
            yield return TimerProbe.Wait(0.4);
            Assert.That(calls, Is.Zero);
            Assert.That(TimerProbe.Cursor(service), Is.EqualTo(before));
            root.enabled = true;
            int frames = 0;
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (calls == 0 && Time.realtimeSinceStartupAsDouble < deadline)
            {
                long cursor = TimerProbe.Cursor(service);
                yield return null;
                Assert.That(TimerProbe.Cursor(service) - cursor, Is.InRange(0, 64));
                frames++;
            }
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(frames, Is.GreaterThanOrEqualTo(4));
            TimerProbe.Ledger(service);
        }

        [UnityTest]
        public IEnumerator BootstrapDisableAndSceneUnloadPreserveApplicationOwnedService()
        {
            CreateRoot();
            var service = (TimerService)AppServices.App.Require<ITimerService>();
            rootObject.GetComponent<TimerComponent>().enabled = false;
            var scene = SceneManager.CreateScene("Timer bootstrap scene");
            componentObject = new GameObject("scene timer bootstrap");
            SceneManager.MoveGameObjectToScene(componentObject, scene);
            componentObject.AddComponent<TimerComponent>();
            ulong h = service.AddTimer(TimerProbe.NoOp, 60, true);
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(AppServices.App.Require<ITimerService>(), Is.SameAs(service));
            Assert.That(service.IsRunning(h), Is.True);
            Object.Destroy(rootObject.GetComponent<TimerComponent>());
            yield return null;
            Assert.That(service.IsRunning(h), Is.True);
            service.RemoveTimer(h);
            TimerProbe.Ledger(service);
        }

        [UnityTest]
        public IEnumerator OwningRootSceneUnloadInvalidatesHandlesAndClearsStoppedLoops()
        {
            CreateRoot(false);
            var scene = SceneManager.CreateScene("Timer owning root scene");
            SceneManager.MoveGameObjectToScene(rootObject, scene);
            var service = (TimerService)AppServices.App.Require<ITimerService>();
            int calls = 0;
            ulong pending = service.AddTimer(() => calls++, 60, isUnscaled: true);
            ulong stopped = service.AddTimer(TimerProbe.CountArg, new TimerArg(), 60, true);
            service.Stop(stopped);
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(AppServices.HasWorld, Is.False);
            Assert.That(service.IsRunning(pending), Is.False);
            service.Restart(stopped);
            Assert.That(service.AddTimer(TimerProbe.NoOp, 1), Is.Zero);
            Assert.That(calls, Is.Zero);
            TimerProbe.Statistics(service, 0, 0);
            TimerProbe.Ledger(service);
        }

        [UnityTest]
        public IEnumerator WorldShutdownInsideRootDrivenCallbackStopsBothWheels()
        {
            CreateRoot();
            var service = (TimerService)AppServices.App.Require<ITimerService>();
            int calls = 0;
            service.AddTimer(() => { calls++; AppServices.Shutdown(); }, 0.001f);
            service.AddTimer(() => calls += 100, 0.03f, isUnscaled: true);
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (calls == 0 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(AppServices.HasWorld, Is.False);
            TimerProbe.Statistics(service, 0, 0);
            TimerProbe.Ledger(service);
        }
    }
}
