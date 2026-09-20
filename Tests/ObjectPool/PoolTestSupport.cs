using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;

[assembly: InternalsVisibleTo("AlicizaX.Framework.ObjectPool.Editor.Tests")]

namespace AlicizaX.ObjectPool.Tests
{
    public sealed class TargetProbe
    {
        public int Spawns, Unspawns, Releases, Shutdowns, Clears;
        public bool ThrowSpawn, ThrowUnspawn, ThrowRelease, ThrowClear;
        public Action SpawnAction, UnspawnAction;
        public Action<bool> ReleaseAction;
        public readonly List<string> Calls = new List<string>();
    }

    public class PoolObject : ObjectBase
    {
        public TargetProbe Probe;
        public bool CanRelease = true;
        public override bool CustomCanReleaseFlag => CanRelease;
        public void Bind(object target, string name = "", bool locked = false)
        {
            Initialize(name, target, locked);
        }
        protected internal override void OnSpawn()
        {
            var probe = Probe;
            if (probe == null) return;
            probe.Spawns++;
            probe.Calls.Add("spawn");
            probe.SpawnAction?.Invoke();
            if (probe.ThrowSpawn) throw new InvalidOperationException("spawn failure");
        }
        protected internal override void OnUnspawn()
        {
            var probe = Probe;
            if (probe == null) return;
            probe.Unspawns++;
            probe.Calls.Add("unspawn");
            probe.UnspawnAction?.Invoke();
            if (probe.ThrowUnspawn) throw new InvalidOperationException("unspawn failure");
        }
        protected internal override void Release(bool isShutdown)
        {
            var probe = Probe;
            if (probe == null) return;
            probe.Releases++;
            if (isShutdown) probe.Shutdowns++;
            probe.Calls.Add(isShutdown ? "shutdown" : "release");
            probe.ReleaseAction?.Invoke(isShutdown);
            if (probe.ThrowRelease) throw new InvalidOperationException("release failure");
        }
        public override void Clear()
        {
            if (Probe != null)
            {
                Probe.Clears++;
                Probe.Calls.Add("clear");
                if (Probe.ThrowClear) throw new InvalidOperationException("clear failure");
            }
            base.Clear();
            Probe = null;
            CanRelease = true;
        }
    }

    public sealed class OtherPoolObject : PoolObject { }

    public sealed class UnityPoolObject : ObjectBase<AudioSource>
    {
        public TargetProbe Probe;
        public void Bind(AudioSource target, string name = "") { Initialize(name, target); }
        protected internal override void OnSpawn() { if (Target != null) Target.gameObject.SetActive(true); Probe.SpawnAction?.Invoke(); }
        protected internal override void OnUnspawn() { if (Target != null) Target.gameObject.SetActive(false); Probe.UnspawnAction?.Invoke(); }
        protected internal override void Release(bool isShutdown)
        {
            Probe.Releases++;
            if (isShutdown) Probe.Shutdowns++;
            Probe.ReleaseAction?.Invoke(isShutdown);
            if (Target != null) UnityEngine.Object.Destroy(Target.gameObject);
            if (Probe.ThrowRelease) throw new InvalidOperationException("release failure");
        }
        public override void Clear() { Probe.Clears++; base.Clear(); Probe = null; }
    }

    public abstract class ObjectPoolFixture
    {
        internal ObjectPoolService Service;
        [SetUp]
        public void SetUp()
        {
            MemoryPoolRegistry.InitializeMainThread();
            MemoryPool<PoolObject>.ClearAll();
            MemoryPool<OtherPoolObject>.ClearAll();
            MemoryPool<UnityPoolObject>.ClearAll();
            Service = new ObjectPoolService();
            ((IServiceLifecycle)Service).Initialize(null, null);
        }
        [TearDown]
        public void TearDown()
        {
            try
            {
                ((IServiceLifecycle)Service).Destroy();
                Assert.That(Using<PoolObject>() + Using<OtherPoolObject>() + Using<UnityPoolObject>(), Is.Zero, "MemoryPool leases must converge before fixture cleanup.");
            }
            finally
            {
                MemoryPool<PoolObject>.ClearAll();
                MemoryPool<OtherPoolObject>.ClearAll();
                MemoryPool<UnityPoolObject>.ClearAll();
            }
        }
        internal IObjectPool<PoolObject> Pool(bool multi = false, int capacity = int.MaxValue, string name = "", float expire = float.MaxValue, float interval = float.MaxValue)
            => Service.GetOrCreatePool<PoolObject>(new ObjectPoolCreateOptions(name, multi, interval, capacity, expire));
        internal static PoolObject Item(string name = "", object target = null, bool locked = false)
        {
            var item = MemoryPool<PoolObject>.Acquire();
            item.Bind(target ?? new object(), name, locked);
            item.Probe = new TargetProbe();
            return item;
        }
        internal static int Using<T>() where T : MemoryObject, new()
        {
            var info = default(MemoryPoolInfo);
            MemoryPool<T>.GetInfo(ref info);
            return info.UsingCount;
        }
        internal void Tick() => ((IServiceTickable)Service).Tick(0);
        internal static T Read<T>(object instance, string field)
            => (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(instance);
        internal static int Active(ObjectPoolService service) => Read<int>(service, "m_ActiveCount");
        internal static int Unused(object pool) => Read<int>(pool, "m_UnusedCount");
        internal static ObjectInfo[] Infos(ObjectPoolBase pool)
        {
            var infos = new ObjectInfo[pool.Count];
            Assert.That(pool.GetAllObjectInfos(infos), Is.EqualTo(infos.Length));
            return infos;
        }

        internal static void Ledger(ObjectPoolBase pool, int count, int unused, int leases = -1)
        {
            Assert.That(pool.Count, Is.EqualTo(count));
            Assert.That(Unused(pool), Is.EqualTo(unused));
            var slots = Read<Array>(pool, "m_Slots");
            if (slots == null)
            {
                Assert.That(count, Is.Zero);
                return;
            }
            var alive = new HashSet<int>();
            var idle = new HashSet<int>();
            var rentable = new HashSet<int>();
            var allRentable = new HashSet<int>();
            var names = new HashSet<string>();
            var map = Read<ReferenceOpenHashMap>(pool, "m_TargetMap");
            int spawnCount = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots.GetValue(i);
                var obj = Read<ObjectBase>(slot, "Obj");
                bool live = (Read<byte>(slot, "Flags") & 1) != 0;
                if (!live) { Assert.That(obj, Is.Null, "Free slot retains wrapper."); continue; }
                alive.Add(i);
                Assert.That(obj, Is.Not.Null);
                Assert.That(obj.Target, Is.Not.Null);
                Assert.That(map.TryGetValue(obj.Target, out int index), Is.True);
                Assert.That(index, Is.EqualTo(i));
                int spawned = Read<int>(slot, "SpawnCount");
                Assert.That(spawned, Is.GreaterThanOrEqualTo(0));
                spawnCount += spawned;
                if (spawned == 0) idle.Add(i);
                if ((Read<byte>(slot, "Flags") & 4) == 0)
                {
                    allRentable.Add(i);
                    if (spawned == 0) rentable.Add(i);
                }
                names.Add(obj.Name ?? "");
            }
            Assert.That(alive.Count, Is.EqualTo(count));
            Assert.That(idle.Count, Is.EqualTo(unused));
            if (leases >= 0) Assert.That(spawnCount, Is.EqualTo(leases));
            var free = Read<int[]>(pool, "m_FreeStack");
            int freeTop = Read<int>(pool, "m_FreeTop");
            var freeSet = new HashSet<int>();
            for (int i = 0; i < freeTop; i++)
            {
                Assert.That(freeSet.Add(free[i]), Is.True);
                Assert.That(alive.Contains(free[i]), Is.False);
            }
            Assert.That(alive.Count + freeSet.Count, Is.EqualTo(slots.Length));
            var walked = Walk(slots, Read<int>(pool, "m_UnusedHead"), "Unused", out int tail);
            Assert.That(walked.SetEquals(idle), Is.True, "Unused chain");
            Assert.That(Read<int>(pool, "m_UnusedTail"), Is.EqualTo(tail));
            var available = new HashSet<int>();
            var all = new HashSet<int>();
            bool named = Read<bool>(pool, "m_HasNameMap");
            var availableMap = Read<OpenHashMap<string>>(pool, "m_AvailableNameHeadMap");
            var allMap = Read<OpenHashMap<string>>(pool, "m_AllNameHeadMap");
            int availableNames = 0, allNames = 0;
            foreach (string name in names)
            {
                int head = -1;
                if (name == "") head = Read<int>(pool, "m_UnnamedAvailableHead");
                else if (named && availableMap.TryGetValue(name, out head)) availableNames++;
                foreach (int index in Walk(slots, head, "Available", out _))
                {
                    Assert.That(available.Add(index), Is.True);
                    Assert.That(Read<ObjectBase>(slots.GetValue(index), "Obj").Name, Is.EqualTo(name));
                }
                if (!pool.AllowMultiSpawn) continue;
                head = -1;
                if (name == "") head = Read<int>(pool, "m_UnnamedAllHead");
                else if (named && allMap.TryGetValue(name, out head)) allNames++;
                foreach (int index in Walk(slots, head, "All", out _)) Assert.That(all.Add(index), Is.True);
            }
            Assert.That(available.SetEquals(rentable), Is.True, "Available chain");
            Assert.That(availableMap.Count, Is.EqualTo(availableNames));
            if (pool.AllowMultiSpawn)
            {
                Assert.That(all.SetEquals(allRentable), Is.True, "All chain");
                Assert.That(allMap.Count, Is.EqualTo(allNames));
            }
            Assert.That(Infos(pool).Length, Is.EqualTo(count));
        }
        private static HashSet<int> Walk(Array slots, int head, string chain, out int tail)
        {
            var result = new HashSet<int>();
            tail = -1;
            while (head >= 0)
            {
                Assert.That(head, Is.LessThan(slots.Length));
                Assert.That(result.Add(head), Is.True, chain + " cycle or duplicate");
                var slot = slots.GetValue(head);
                Assert.That(Read<int>(slot, "Prev" + chain), Is.EqualTo(tail));
                tail = head;
                head = Read<int>(slot, "Next" + chain);
            }
            return result;
        }
    }
}
