using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class ObjectPoolLifecycleTests : ObjectPoolFixture
    {
        [UnityTest]
        public IEnumerator UnityCallbackShutdownDefersTargetDestructionUntilCallbackCompletes()
        {
            var pool = Service.GetOrCreatePool<UnityPoolObject>();
            var source = new GameObject("shutdown-from-unspawn").AddComponent<AudioSource>();
            var obj = MemoryPool<UnityPoolObject>.Acquire();
            var probe = obj.Probe = new TargetProbe();
            obj.Bind(source);
            pool.Register(obj, true);
            probe.UnspawnAction = () =>
            {
                ((IServiceLifecycle)Service).Destroy();
                Assert.That(obj.Target, Is.SameAs(source));
                Assert.That(Using<UnityPoolObject>(), Is.EqualTo(1));
            };
            pool.Unspawn(obj);
            Assert.That(probe.Shutdowns, Is.EqualTo(1));
            Assert.That(probe.Clears, Is.EqualTo(1));
            Assert.That(Using<UnityPoolObject>(), Is.Zero);
            yield return null;
            Assert.That(source == null, Is.True);
        }

        [UnityTest]
        public IEnumerator WorldShutdownReleasesInUseIdleAndLockedUnityTargets()
        {
            Assert.That(AppServices.HasWorld, Is.False);
            ((IServiceLifecycle)Service).Destroy();
            Service = new ObjectPoolService();
            AppServices.EnsureWorld().App.Register<IObjectPoolService>(Service);
            var pool = Service.GetOrCreatePool<UnityPoolObject>();
            var probes = new TargetProbe[3];
            var sources = new AudioSource[3];
            for (int i = 0; i < 3; i++)
            {
                sources[i] = new GameObject("shutdown-unity-" + i).AddComponent<AudioSource>();
                var obj = MemoryPool<UnityPoolObject>.Acquire();
                probes[i] = obj.Probe = new TargetProbe();
                obj.Bind(sources[i]);
                obj.Locked = i == 2;
                pool.Register(obj, i == 0);
            }
            AppServices.Shutdown();
            Assert.That(AppServices.HasWorld, Is.False);
            Assert.That(Using<UnityPoolObject>(), Is.Zero);
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            yield return null;
            for (int i = 0; i < 3; i++)
            {
                Assert.That(sources[i] == null, Is.True);
                Assert.That(probes[i].Shutdowns, Is.EqualTo(1));
                Assert.That(probes[i].Clears, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator UnityTargetSpawnRegisterReplaceAndShutdownRecycleWrappers()
        {
            var pool = Service.GetOrCreatePool<UnityPoolObject>(new ObjectPoolCreateOptions("audio", false, 10, int.MaxValue, float.MaxValue, 10));
            Assert.That(pool.Spawn("source"), Is.Null);
            var source = new GameObject("object-pool-audio").AddComponent<AudioSource>();
            var obj = MemoryPool<UnityPoolObject>.Acquire();
            var probe = obj.Probe = new TargetProbe();
            obj.Bind(source, "source");
            Assert.That(pool.Register(obj, true), Is.True);
            Ledger((ObjectPoolBase)pool, 1, 0, 1);
            pool.Unspawn(obj);
            Assert.That(source.gameObject.activeSelf, Is.False);
            Assert.That(pool.Spawn("source"), Is.SameAs(obj));
            Assert.That(source.gameObject.activeSelf, Is.True);
            pool.UnspawnTarget(source);
            pool.ReleaseAllUnused();
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(Using<UnityPoolObject>(), Is.Zero);
            Assert.That(probe.Releases, Is.EqualTo(1));
            Assert.That(probe.Clears, Is.EqualTo(1));
            yield return null;
            Assert.That(source == null, Is.True);
        }

        [UnityTest]
        public IEnumerator ExternallyDestroyedUnityTargetRetainsReferenceUntilExplicitRelease()
        {
            var pool = Service.GetOrCreatePool<UnityPoolObject>();
            var source = new GameObject("object-pool-external-destroy").AddComponent<AudioSource>();
            var obj = MemoryPool<UnityPoolObject>.Acquire();
            var probe = obj.Probe = new TargetProbe();
            obj.Bind(source);
            Assert.That(pool.Register(obj, true), Is.True);
            UnityEngine.Object.Destroy(source.gameObject);
            yield return null;
            Assert.That(source == null, Is.True);
            Assert.That(ReferenceEquals(((ObjectBase)obj).Target, null), Is.False);
            pool.UnspawnTarget(source);
            Ledger((ObjectPoolBase)pool, 1, 1, 0);
            pool.ReleaseAllUnused();
            Ledger((ObjectPoolBase)pool, 0, 0, 0);
            Assert.That(probe.Releases, Is.EqualTo(1));
            Assert.That(Using<UnityPoolObject>(), Is.Zero);
        }
    }
}
