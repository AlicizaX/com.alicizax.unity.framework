using System;
using AlicizaX.Resource.Runtime;
using AlicizaX.Scene.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlicizaX.Scene.Tests
{
    public sealed class SceneServiceTests
    {
        private ServiceRegistry _registry;
        private ISceneService _service;

        [SetUp]
        public void SetUp()
        {
            _registry = new ServiceRegistry();
            var resources = new ResourceService();
            _registry.Register<IResourceService, IResourcePackageService>(resources);
            _service = _registry.Register<ISceneService>(new SceneService());
        }

        [TearDown]
        public void TearDown()
        {
            _registry?.Dispose();
            _registry = null;
            _service = null;
        }

        [Test]
        public void Contains_rejects_empty_and_unknown_location()
        {
            Assert.That(_service.Contains(null), Is.False);
            Assert.That(_service.Contains(string.Empty), Is.False);
            Assert.That(_service.Contains("missing"), Is.False);
        }

        [Test]
        public void MainScene_falls_back_to_active_scene_before_any_load()
        {
            Assert.That(_service.MainScene.name, Is.EqualTo(SceneManager.GetActiveScene().name));
        }

        [Test]
        public void SetActive_and_AllowActivation_return_false_for_unknown_location()
        {
            Assert.That(_service.SetActive("missing"), Is.False);
            Assert.That(_service.AllowActivation("missing"), Is.False);
            Assert.That(_service.SetActive(null), Is.False);
            Assert.That(_service.AllowActivation(null), Is.False);
        }

        [Test]
        public void Load_entry_points_reject_empty_location_synchronously()
        {
            Assert.Throws<ArgumentNullException>(() => _service.LoadMainAsync(string.Empty));
            Assert.Throws<ArgumentNullException>(() => _service.ReloadMainAsync(string.Empty));
            Assert.Throws<ArgumentNullException>(() => _service.LoadAdditiveAsync(string.Empty));
            Assert.Throws<ArgumentNullException>(() => _service.UnloadAdditiveAsync(string.Empty));
            Assert.Throws<ArgumentNullException>(() => _service.LoadMainAsync(null));
        }

        [Test]
        public void Unload_rejects_unknown_location_before_touching_yooasset()
        {
            Assert.Throws<GameFrameworkException>(() => _service.UnloadAdditiveAsync("missing"));
        }

        [Test]
        public void Destroy_clears_scene_tracking()
        {
            _registry.Dispose();

            Assert.That(_service.Contains("missing"), Is.False);
            Assert.That(_service.MainScene.name, Is.EqualTo(SceneManager.GetActiveScene().name));
        }

        [Test]
        public void Calls_after_destroy_report_object_disposed()
        {
            _registry.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _service.LoadMainAsync("missing").GetAwaiter().GetResult());
            Assert.Throws<ObjectDisposedException>(() => _service.LoadAdditiveAsync("missing").GetAwaiter().GetResult());
        }

        [Test]
        public void SceneComponent_initializes_after_resource_module()
        {
            var order = (DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(SceneComponent), typeof(DefaultExecutionOrder));

            Assert.That(order, Is.Not.Null);
            Assert.That(order.order, Is.EqualTo(-600));
        }
    }
}
