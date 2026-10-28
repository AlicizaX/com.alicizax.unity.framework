using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Tests
{
    public sealed class ServiceScopeExceptionTests
    {
        private ServiceWorld world;

        [SetUp]
        public void SetUp()
        {
            ServiceScope.DisableFaultedServices = false;
            world = new ServiceWorld();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceScope.DisableFaultedServices = false;
            world?.Dispose();
            world = null;
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ThrowingTickableDoesNotStopRemainingServicesOrTheTickLoop()
        {
            var bad = new ThrowingTickable();
            var good = new CountingTickable();
            world.App.RegisterSelf(bad);
            world.App.RegisterSelf(good);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            world.Tick(1f);

            Assert.That(bad.TickCount, Is.EqualTo(1));
            Assert.That(good.TickCount, Is.EqualTo(1), "the service after the throwing one must still tick");

            // 同一服务再次失败：仍必须调用它（不能被永久禁用），且不能重复记录日志。
            world.Tick(1f);
            Assert.That(bad.TickCount, Is.EqualTo(2));
            Assert.That(good.TickCount, Is.EqualTo(2));
        }

        [Test]
        public void RegistrationFromInsideAThrowingTickIsFlushed()
        {
            var late = new CountingTickable();
            var bad = new RegisteringThrowingTickable(() => world.App.RegisterSelf(late));
            world.App.RegisterSelf(bad);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            world.Tick(1f);

            Assert.That(late.Initialized, Is.True, "pending registrations must flush even when a tick throws");
            Assert.That(late.TickCount, Is.EqualTo(0), "a service registered during a tick only starts ticking next frame");

            world.Tick(1f);
            Assert.That(late.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void DisableFaultedServicesSkipsTheFaultedServiceOnly()
        {
            ServiceScope.DisableFaultedServices = true;
            var bad = new ThrowingTickable();
            var good = new CountingTickable();
            world.App.RegisterSelf(bad);
            world.App.RegisterSelf(good);

            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            world.Tick(1f);
            world.Tick(1f);

            Assert.That(bad.TickCount, Is.EqualTo(1));
            Assert.That(good.TickCount, Is.EqualTo(2));
        }

        [Test]
        public void ReRegisteringAFaultedServiceClearsTheFault()
        {
            ServiceScope.DisableFaultedServices = true;
            var bad = new ThrowingTickable();
            world.App.RegisterSelf(bad);
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            world.Tick(1f);

            world.App.Unregister(bad);
            world.App.RegisterSelf(bad);
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            world.Tick(1f);

            Assert.That(bad.TickCount, Is.EqualTo(2));
        }

        [Test]
        public void DisposeContinuesAfterALifecycleException()
        {
            var bad = new ThrowingDestroyService();
            var good = new CountingTickable();
            world.App.RegisterSelf(bad);
            world.App.RegisterSelf(good);

            LogAssert.Expect(LogType.Exception, new Regex("destroy-boom"));
            world.Dispose();

            Assert.That(good.Destroyed, Is.True, "one broken service must not abort scope disposal");
        }

        private sealed class CountingTickable : IService, IServiceTickable, IServiceLifecycle
        {
            public int TickCount;
            public bool Initialized;
            public bool Destroyed;

            public void Tick(float deltaTime) => TickCount++;

            public void Initialize(ServiceWorld world, ServiceScope scope) => Initialized = true;

            public void Destroy() => Destroyed = true;
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
            private readonly Action register;

            public RegisteringThrowingTickable(Action register) => this.register = register;

            public void Tick(float deltaTime)
            {
                register();
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class ThrowingDestroyService : IService, IServiceLifecycle
        {
            public void Initialize(ServiceWorld world, ServiceScope scope) { }

            public void Destroy() => throw new InvalidOperationException("destroy-boom");
        }
    }
}
