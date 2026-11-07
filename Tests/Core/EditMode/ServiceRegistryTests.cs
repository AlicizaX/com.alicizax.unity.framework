using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Tests
{
    public sealed class ServiceRegistryTests
    {
        private ServiceRegistry registry;

        [SetUp]
        public void SetUp()
        {
            registry = new ServiceRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            registry?.Dispose();
            registry = null;
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ThrowingTickableDoesNotStopRemainingServicesOrTheTickLoop()
        {
            var bad = new ThrowingTickable();
            var good = new CountingTickable();
            registry.Register(bad);
            registry.Register(good);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            registry.Tick(1f);

            Assert.That(bad.TickCount, Is.EqualTo(1));
            Assert.That(good.TickCount, Is.EqualTo(1), "the service after the throwing one must still tick");

            // 同一服务再次失败：仍必须调用它（不能被永久禁用），且不能重复记录日志。
            registry.Tick(1f);
            Assert.That(bad.TickCount, Is.EqualTo(2));
            Assert.That(good.TickCount, Is.EqualTo(2));
        }

        [Test]
        public void RegistrationFromInsideATickStartsTickingNextFrame()
        {
            var late = new CountingTickable();
            var bad = new RegisteringThrowingTickable(() => registry.Register(late));
            registry.Register(bad);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            registry.Tick(1f);

            Assert.That(late.Initialized, Is.True, "注册立即完成初始化");
            Assert.That(late.TickCount, Is.EqualTo(0), "tick 中注册的服务本帧不参与 tick");

            registry.Tick(1f);
            Assert.That(late.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void UnregisterDuringTickTakesEffectAfterTheTickLoop()
        {
            var victim = new CountingTickable();
            var remover = new ActionTickable(() => registry.Unregister(victim));
            registry.Register(remover);
            registry.Register(victim);

            registry.Tick(1f);

            Assert.That(victim.TickCount, Is.EqualTo(1), "延迟注销：本帧剩余阶段仍会收到 tick");
            Assert.That(registry.TryGet(out CountingTickable _), Is.False, "注销在 tick 循环结束后完成");

            registry.Tick(1f);
            Assert.That(victim.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void TickOrderFollowsRegistrationOrder()
        {
            var order = new List<int>();
            IService[] services =
            {
                new OrderTickable<Tag0>(0, order),
                new OrderTickable<Tag1>(1, order),
                new OrderTickable<Tag2>(2, order),
                new OrderTickable<Tag3>(3, order),
                new OrderTickable<Tag4>(4, order),
                new OrderTickable<Tag5>(5, order),
            };

            foreach (var service in services)
                registry.Register(service);

            registry.Unregister(services[2]);
            registry.Tick(1f);

            Assert.That(order, Is.EqualTo(new[] { 0, 1, 3, 4, 5 }), "注销后剩余服务的相对顺序不能变化");
        }

        [Test]
        public void TickLoopDoesNotAllocate()
        {
            registry.Register(new CountingTickable());
            registry.Register(new ActionTickable(() => { }));

            // 预热：JIT、首次迭代与列表扩容都在测量之外。
            registry.Tick(0.016f);
            registry.Tick(0.016f);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
                registry.Tick(0.016f);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, $"1000 次 tick 共分配 {allocated} B");
        }

        [Test]
        public void ResolvingServicesDoesNotAllocate()
        {
            registry.Register(new CountingTickable());
            registry.Require<CountingTickable>();
            registry.TryGet(out CountingTickable _);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++)
            {
                registry.Require<CountingTickable>();
                registry.TryGet(out CountingTickable _);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, $"20000 次解析分配了 {allocated} B");
        }

        [Test]
        public void MonoServiceRegistersOnAwakeAndUnregistersOnDestroy()
        {
            if (AppServices.IsAvailable) AppServices.Shutdown();
            AppServices.GetOrCreateRegistry();

            GameObject root = null;
            try
            {
                // 失活对象上 AddComponent 不会触发 Awake，用例显式驱动生命周期。
                root = new GameObject("MonoServiceProbe");
                root.SetActive(false);
                var probe = root.AddComponent<ProbeMonoService>();

                probe.InvokeAwake();

                Assert.That(AppServices.Require<IAlphaService>(), Is.SameAs(probe), "Mono 服务的接口契约与普通服务解析方式一致");
                Assert.That(AppServices.Require<ProbeMonoService>(), Is.SameAs(probe), "具体类型同样可解析");

                probe.InvokeDestroy();

                Assert.That(AppServices.TryGet(out IAlphaService _), Is.False, "OnDestroy 必须注销契约");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                AppServices.Shutdown();
            }
        }

        [Test]
        public void MonoServiceCannotImplementTickLifecycleInterfaces()
        {
            if (AppServices.IsAvailable) AppServices.Shutdown();
            AppServices.GetOrCreateRegistry();

            GameObject root = null;
            try
            {
                root = new GameObject("MonoTickProbe");
                root.SetActive(false);
                var probe = root.AddComponent<TickMonoService>();

                Assert.Throws<InvalidOperationException>(probe.InvokeAwake, "Unity 与容器不能同时驱动同一个服务");
                Assert.That(AppServices.TryGet(out IAlphaService _), Is.False);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                AppServices.Shutdown();
            }
        }

        [Test]
        public void RegisteringAnInstanceThatDoesNotImplementTheContractThrows()
        {
            var service = new AlphaOnlyService();

            Assert.Throws<ArgumentException>(() => registry.Register<IBetaService>(service));
            Assert.That(registry.TryGet(out IBetaService _), Is.False);
        }

        [Test]
        public void FailedInitializeRollsBackTheRegistration()
        {
            Assert.Throws<InvalidOperationException>(() => registry.Register(new ThrowingInitializeService()));

            Assert.That(registry.TryGet(out ThrowingInitializeService _), Is.False);
        }

        [Test]
        public void DisposeContinuesAfterALifecycleException()
        {
            var bad = new ThrowingDisposeService();
            var good = new CountingTickable();
            registry.Register(bad);
            registry.Register(good);

            LogAssert.Expect(LogType.Exception, new Regex("dispose-boom"));
            registry.Dispose();

            Assert.That(good.Disposed, Is.True, "one broken service must not abort registry disposal");
        }

        [Test]
        public void RegistrationDuringDisposeIsRejected()
        {
            var service = new RegisteringOnDisposeService();
            registry.Register(service);

            registry.Dispose();

            Assert.That(service.CapturedException, Is.TypeOf<ObjectDisposedException>(),
                "销毁中的注册必须显式失败，否则服务会被追加到正在销毁的列表里");
        }

        private interface IAlphaService : IService
        {
        }

        private interface IBetaService : IService
        {
        }

        private sealed class AlphaOnlyService : IAlphaService
        {
        }

        private sealed class ProbeMonoService : MonoServiceBehaviour<IAlphaService>, IAlphaService
        {
            public void InvokeAwake() => Awake();

            public void InvokeDestroy() => OnDestroy();
        }

        private sealed class TickMonoService : MonoServiceBehaviour<IAlphaService>, IAlphaService, IServiceTickable
        {
            public void InvokeAwake() => Awake();

            public void Tick(float deltaTime)
            {
            }
        }

        private sealed class CountingTickable : IService, IServiceTickable, IServiceLifecycle
        {
            public int TickCount;
            public bool Initialized;
            public bool Disposed;

            public void Tick(float deltaTime) => TickCount++;

            public void Initialize(ServiceRegistry registry) => Initialized = true;

            public void Dispose() => Disposed = true;
        }

        private sealed class OrderTickable<TTag> : IService, IServiceTickable
        {
            private readonly int _id;
            private readonly List<int> _order;

            public OrderTickable(int id, List<int> order)
            {
                _id = id;
                _order = order;
            }

            public void Tick(float deltaTime) => _order.Add(_id);
        }

        private sealed class Tag0
        {
        }

        private sealed class Tag1
        {
        }

        private sealed class Tag2
        {
        }

        private sealed class Tag3
        {
        }

        private sealed class Tag4
        {
        }

        private sealed class Tag5
        {
        }

        private sealed class ActionTickable : IService, IServiceTickable
        {
            private readonly Action _action;

            public ActionTickable(Action action) => _action = action;

            public void Tick(float deltaTime) => _action();
        }

        private sealed class ThrowingTickable : IService, IServiceTickable
        {
            public int TickCount;

            public void Tick(float deltaTime)
            {
                TickCount++;
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class RegisteringThrowingTickable : IService, IServiceTickable
        {
            private readonly Action _register;

            public RegisteringThrowingTickable(Action register) => _register = register;

            public void Tick(float deltaTime)
            {
                _register();
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class ThrowingInitializeService : IService, IServiceLifecycle
        {
            public void Initialize(ServiceRegistry registry) => throw new InvalidOperationException("init-boom");

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingDisposeService : IService, IServiceLifecycle
        {
            public void Initialize(ServiceRegistry registry)
            {
            }

            public void Dispose() => throw new InvalidOperationException("dispose-boom");
        }

        private sealed class RegisteringOnDisposeService : IService, IServiceLifecycle
        {
            private ServiceRegistry _registry;

            public Exception CapturedException { get; private set; }

            public void Initialize(ServiceRegistry registry) => _registry = registry;

            public void Dispose()
            {
                try
                {
                    _registry.Register(new CountingTickable());
                }
                catch (Exception exception)
                {
                    CapturedException = exception;
                }
            }
        }
    }
}
