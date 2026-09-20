using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using AlicizaX;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

[assembly: InternalsVisibleTo("AlicizaX.Framework.GameObjectPool.Editor.Tests")]

namespace AlicizaX.GameObjectPool.Tests
{
    internal sealed class ControlledPrefabLoader : IPrefabLoader
    {
        internal readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        internal int LoadCalls;
        internal int AsyncLoadCalls;
        internal int UnloadCalls;
        internal int LiveLeases { get; private set; }
        internal bool ThrowOnLoad;
        internal bool CompleteImmediately = true;
        internal sealed class Request
        {
            internal readonly string Location;
            internal readonly GameObject Prefab;
            internal readonly UniTaskCompletionSource<GameObject> Completion = new UniTaskCompletionSource<GameObject>();
            internal Request(string location, GameObject prefab) { Location = location; Prefab = prefab; }
        }

        internal readonly List<Request> Pending = new List<Request>();
        private readonly Dictionary<GameObject, int> _leases = new Dictionary<GameObject, int>();

        public GameObject LoadPrefab(string location)
        {
            LoadCalls++;
            if (ThrowOnLoad)
                throw new InvalidOperationException("controlled load failure");
            if (!Prefabs.TryGetValue(location, out GameObject prefab) || prefab == null)
                return null;
            Retain(prefab);
            return prefab;
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            AsyncLoadCalls++;
            if (ThrowOnLoad)
                throw new InvalidOperationException("controlled load failure");
            if (!CompleteImmediately)
            {
                Prefabs.TryGetValue(location, out GameObject asset);
                var request = new Request(location, asset);
                Pending.Add(request);
                GameObject delayed = await request.Completion.Task;
                if (delayed != null)
                    Retain(delayed);
                return delayed;
            }

            if (!Prefabs.TryGetValue(location, out GameObject prefab) || prefab == null)
                return null;
            Retain(prefab);
            return prefab;
        }

        public void CompletePending(bool success = true)
        {
            Assert.That(Pending.Count, Is.EqualTo(1), "Select a request explicitly when several loads are pending.");
            Complete(Pending[0], success);
        }

        internal void Complete(Request request, bool success = true)
        {
            Assert.That(Pending.Remove(request), Is.True, "Request was already completed or belongs to another loader.");
            Assert.That(request.Completion.TrySetResult(success ? request.Prefab : null), Is.True);
        }

        public void FailPending()
        {
            Assert.That(Pending.Count, Is.EqualTo(1));
            Request request = Pending[0];
            Pending.RemoveAt(0);
            Assert.That(request.Completion.TrySetException(new InvalidOperationException("controlled async failure")), Is.True);
        }

        public bool HasPending => Pending.Count != 0;

        internal int LeaseCount(GameObject prefab) => _leases.TryGetValue(prefab, out int count) ? count : 0;

        private void Retain(GameObject prefab)
        {
            _leases[prefab] = LeaseCount(prefab) + 1;
            LiveLeases++;
        }

        public void UnloadPrefab(GameObject prefab)
        {
            Assert.That(ReferenceEquals(prefab, null), Is.False, "Cannot unload a null lease.");
            Assert.That(LeaseCount(prefab), Is.GreaterThan(0), "Prefab was not leased or was released twice.");
            UnloadCalls++;
            _leases[prefab]--;
            LiveLeases--;
        }
    }

    internal sealed class PoolableProbe : MonoBehaviour, IGameObjectPoolable
    {
        public int Spawns;
        public int Despawns;
        public int Destroys;
        public bool ThrowSpawn;
        public bool ThrowDespawn;
        public bool ThrowDestroy;
        public readonly List<string> Calls = new List<string>();
        public PoolSpawnContext LastContext;

        public void OnSpawn(in PoolSpawnContext context)
        {
            Spawns++;
            LastContext = context;
            Calls.Add("spawn");
            if (ThrowSpawn)
                throw new InvalidOperationException("spawn failure");
        }

        public void OnDespawn()
        {
            Despawns++;
            Calls.Add("despawn");
            if (ThrowDespawn)
                throw new InvalidOperationException("despawn failure");
        }

        public void OnPooledDestroy()
        {
            Destroys++;
            Calls.Add("destroy");
            if (ThrowDestroy)
                throw new InvalidOperationException("destroy failure");
        }
    }

    internal sealed class ChildOnlyMarker : MonoBehaviour
    {
    }

    internal sealed class RootMarker : MonoBehaviour
    {
    }

    public abstract class GameObjectPoolFixture
    {
        internal GameObjectPoolService Service;
        internal ControlledPrefabLoader Loader;
        internal Transform Container;
        internal readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        internal PoolConfigScriptableObject Config;

        [SetUp]
        public void SetUpPool()
        {
            MemoryPoolRegistry.InitializeMainThread();
            MemoryPool<RuntimeGameObjectPool>.ClearAll();
            MemoryPool<GameObjectPoolSnapshot>.ClearAll();
            MemoryPool<GameObjectPoolInstanceSnapshot>.ClearAll();
            Loader = new ControlledPrefabLoader();
            var containerObject = Keep(new GameObject("gop-container"));
            Container = containerObject.transform;
            Service = new GameObjectPoolService(Container, Loader);
            ((IServiceLifecycle)Service).Initialize(null, null);
            Config = ScriptableObject.CreateInstance<PoolConfigScriptableObject>();
            Owned.Add(Config);
        }

        [TearDown]
        public void TearDownPool()
        {
            var instances = new List<GameObject>();
            if (Service != null)
            {
                foreach (GameObjectPoolHandle handle in UnityEngine.Object.FindObjectsOfType<GameObjectPoolHandle>(true))
                {
                    var owner = Read<RuntimeGameObjectPool>(handle, "_owner");
                    if (owner != null && ReferenceEquals(Read<GameObjectPoolService>(owner, "_service"), Service))
                        instances.Add(handle.gameObject);
                }
            }
            try
            {
                if (Service != null)
                    ((IServiceLifecycle)Service).Destroy();
                Assert.That(Using<RuntimeGameObjectPool>(), Is.Zero, "RuntimeGameObjectPool leases must converge.");
                Assert.That(Using<GameObjectPoolSnapshot>() + Using<GameObjectPoolInstanceSnapshot>(), Is.Zero, "Debug snapshots must be released.");
                Assert.That(Loader.LiveLeases, Is.Zero, "Service shutdown left prefab leases.");
                Assert.That(Loader.HasPending, Is.False, "The test left an undelivered backend request.");
            }
            finally
            {
                Service = null;
                foreach (GameObject instance in instances)
                    if (instance != null) UnityEngine.Object.DestroyImmediate(instance);

                for (int i = 0; i < Owned.Count; i++)
                {
                    if (Owned[i] != null)
                        UnityEngine.Object.DestroyImmediate(Owned[i]);
                }

                Owned.Clear();
                MemoryPool<RuntimeGameObjectPool>.ClearAll();
                MemoryPool<GameObjectPoolSnapshot>.ClearAll();
                MemoryPool<GameObjectPoolInstanceSnapshot>.ClearAll();
            }
        }

        internal T Keep<T>(T value) where T : UnityEngine.Object
        {
            Owned.Add(value);
            return value;
        }

        internal GameObject Prefab(string location, bool poolable = false, bool childMarker = false, bool rootMarker = false)
        {
            var go = Keep(new GameObject(location.Replace('/', '_')));
            if (poolable)
                go.AddComponent<PoolableProbe>();
            if (rootMarker)
                go.AddComponent<RootMarker>();
            if (childMarker)
            {
                var child = new GameObject("child");
                child.transform.SetParent(go.transform, false);
                child.AddComponent<ChildOnlyMarker>();
            }

            Loader.Prefabs[location] = go;
            return go;
        }

        internal static PoolEntry Entry(
            string path,
            PoolPolicy policy = PoolPolicy.Burst,
            int minIdle = 0,
            int soft = 8,
            int hard = 16,
            float idle = 15f,
            bool unload = true,
            int priority = 0,
            string group = null,
            string name = null)
        {
            var entry = new PoolEntry
            {
                entryName = name ?? PoolEntry.DefaultEntryName,
                group = group ?? PoolEntry.DefaultGroup,
                assetPath = path,
                policy = policy,
                minIdle = minIdle,
                softCapacity = soft,
                hardCapacity = hard,
                idleSeconds = idle,
                unloadPrefab = unload,
                priority = priority
            };
            entry.Normalize();
            return entry;
        }

        internal void Catalog(params PoolEntry[] entries)
        {
            Config.entries = new List<PoolEntry>(entries);
            Service.LoadCatalog(Config);
        }

        internal void Tick() => ((IServiceTickable)Service).Tick(0f);

        internal GameObject MustSpawn(string location, Transform parent = null)
        {
            GameObject instance = Service.Spawn(location, parent);
            Assert.That(instance, Is.Not.Null, location);
            return instance;
        }

        internal GameObject[] FillIdle(string location, int count)
        {
            Assert.That(Service.LoadPrefab(location), Is.Not.Null, location);
            var spawned = new GameObject[count];
            for (int i = 0; i < count; i++)
                spawned[i] = MustSpawn(location);
            for (int i = 0; i < count; i++)
                Service.Despawn(spawned[i]);
            Ledger(location, count, 0, count, true);
            return spawned;
        }

        internal void TickUntil(string location, Func<RuntimeGameObjectPool, bool> done, int maxTicks = 32)
        {
            RuntimeGameObjectPool pool = RuntimePool(location);
            Assert.That(pool, Is.Not.Null, location);
            for (int i = 0; i < maxTicks && !done(pool); i++)
                Tick();
            Assert.That(done(pool), Is.True, "condition not met after " + maxTicks + " ticks");
        }

        internal IGameObjectPoolDebugService DebugService => Service;

        internal GameObjectPoolSummarySnapshot Summary() => DebugService.GetDebugSummary();

        internal RuntimeGameObjectPool RuntimePool(string location)
        {
            string normalized = PoolEntry.NormalizeLocation(location);
            var pools = Read<RuntimeGameObjectPool[]>(Service, "_pools");
            int count = Read<int>(Service, "_poolCount");
            for (int i = 0; i < count; i++)
            {
                if (pools[i] != null && pools[i].Location == normalized)
                    return pools[i];
            }

            return null;
        }

        internal void Ledger(string location, int total, int active, int inactive, bool prefabLoaded)
        {
            var pool = RuntimePool(location);
            Assert.That(pool, Is.Not.Null, "pool " + location);
            Assert.That(pool.TotalCount, Is.EqualTo(total), "total");
            Assert.That(pool.ActiveCount, Is.EqualTo(active), "active");
            Assert.That(pool.InactiveCount, Is.EqualTo(inactive), "inactive");
            Assert.That(pool.IsPrefabLoaded, Is.EqualTo(prefabLoaded), "prefab");
            Assert.That(active + inactive, Is.EqualTo(total), "active+inactive");
            Assert.That(total, Is.GreaterThanOrEqualTo(0));
            Assert.That(active, Is.GreaterThanOrEqualTo(0));
            Assert.That(inactive, Is.GreaterThanOrEqualTo(0));
        }

        internal static T Read<T>(object instance, string field)
            => (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(instance);

        internal static void Write(object instance, string field, object value)
            => instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);

        internal static int Using<T>() where T : MemoryObject, new()
        {
            var info = default(MemoryPoolInfo);
            MemoryPool<T>.GetInfo(ref info);
            return info.UsingCount;
        }

        internal static IEnumerator Wait(UniTask task)
        {
            return Wait(task, 180);
        }

        internal static IEnumerator Wait(UniTask task, int maxFrames)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < maxFrames; frame++)
                yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "Operation did not terminate.");
            awaiter.GetResult();
        }

        internal static IEnumerator Wait<T>(UniTask<T> task, Action<T> result)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 180; frame++)
                yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "Operation did not terminate.");
            result(awaiter.GetResult());
        }

        internal static IEnumerator WaitCanceled<T>(UniTask<T> task)
        {
            var awaiter = task.GetAwaiter();
            for (int frame = 0; !awaiter.IsCompleted && frame < 180; frame++)
                yield return null;
            Assert.That(awaiter.IsCompleted, Is.True, "Cancel did not terminate.");
            Assert.Throws<OperationCanceledException>(() => awaiter.GetResult());
        }

        internal int DestroyCount(string location) => Read<int>(RuntimePool(location), "_destroyCount");
        internal int HitCount(string location) => Read<int>(RuntimePool(location), "_hitCount");
        internal int MissCount(string location) => Read<int>(RuntimePool(location), "_missCount");
        internal int SpawnStat(string location) => Read<int>(RuntimePool(location), "_spawnCount");
        internal int DespawnStat(string location) => Read<int>(RuntimePool(location), "_despawnCount");
        internal int ExpandCount(string location) => Read<int>(RuntimePool(location), "_expandCount");
        internal int MaintenanceCount() => Read<int>(Service, "_maintenanceCount");
        internal int InactiveHead(string location) => Read<int>(RuntimePool(location), "_inactiveHead");
        internal int InactiveTail(string location) => Read<int>(RuntimePool(location), "_inactiveTail");
        internal uint Generation(string location) => Read<uint>(RuntimePool(location), "_generationCounter");
        internal bool PrefabLoading(string location) => Read<UniTaskCompletionSource<GameObject>>(RuntimePool(location), "_prefabLoadCompletionSource") != null;
        internal int LoadVersion(string location) => Read<int>(RuntimePool(location), "_loadVersion");
        internal int RetainTarget(string location) => Read<int>(RuntimePool(location), "_retainTarget");

        internal void RaiseLowMemory()
        {
            typeof(GameObjectPoolService).GetMethod("OnLowMemory", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(Service, null);
        }

        internal Transform GroupRoot(string group)
        {
            string groupName = string.IsNullOrWhiteSpace(group) ? PoolEntry.DefaultGroup : group.Trim();
            for (int i = 0; i < Container.childCount; i++)
            {
                Transform child = Container.GetChild(i);
                if (child.name == "[" + groupName + "]")
                    return child;
            }

            return null;
        }
    }
}
