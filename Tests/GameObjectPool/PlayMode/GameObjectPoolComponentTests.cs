using System.Collections;
using System.Text.RegularExpressions;
using AlicizaX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class GameObjectPoolTestRoot : AppServiceRoot
    {
    }

    public sealed class GameObjectPoolComponentTests
    {
        private GameObject _root;
        private GameObject _first;
        private GameObject _second;

        [SetUp]
        public void SetUp()
        {
            MemoryPoolRegistry.InitializeMainThread();
            Assert.That(AppServices.IsAvailable, Is.False);
        }

        [UnityTearDown]
        public IEnumerator CleanupScene()
        {
            if (_second != null) UnityEngine.Object.Destroy(_second);
            if (_first != null) UnityEngine.Object.Destroy(_first);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            yield return null;
            AppServices.Shutdown();
        }

        [UnityTest]
        public IEnumerator DuplicateComponentThrowsAndDoesNotReplaceTheRegisteredService()
        {
            _root = new GameObject("gop-root");
            _root.AddComponent<GameObjectPoolTestRoot>();
            _first = new GameObject("gop-component-owner");
            _first.AddComponent<GameObjectPoolComponent>();
            var registered = AppServices.Require<IGameObjectPoolService>();
            _second = new GameObject("gop-component-duplicate");
            LogAssert.Expect(LogType.Exception, new Regex("is already bound to"));
            _second.AddComponent<GameObjectPoolComponent>();
            Assert.That(AppServices.Require<IGameObjectPoolService>(), Is.SameAs(registered));
            Assert.That(AppServices.Require<IGameObjectPoolDebugService>(), Is.SameAs(registered));
            yield return null;
        }

        [UnityTest]
        public IEnumerator DestroyingComponentDoesNotUnregisterTheService()
        {
            AppServices.GetOrCreateRegistry();
            _first = new GameObject("gop-component-owner");
            _first.AddComponent<GameObjectPoolComponent>();
            var registered = AppServices.Require<IGameObjectPoolService>();
            UnityEngine.Object.Destroy(_first);
            yield return null;
            Assert.That(_first == null, Is.True);
            Assert.That(AppServices.TryGet<IGameObjectPoolService>(out var remaining), Is.True);
            Assert.That(remaining, Is.SameAs(registered));
        }
    }
}
