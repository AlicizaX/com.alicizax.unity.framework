using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;

[assembly: InternalsVisibleTo("AlicizaX.Framework.MemoryPool.Editor.Tests")]

namespace AlicizaX.MemoryPoolTests
{
    public class PoolItem : MemoryObject, IPoolEvictable
    {
        public object Payload;
        public Action OnClear;
        public Action OnEviction;
        public int Clears;
        public int Evictions;

        public override void Clear()
        {
            OnClear?.Invoke();
            Payload = null;
            Clears++;
        }

        public void OnEvict()
        {
            OnEviction?.Invoke();
            Evictions++;
        }
    }

    public sealed class OtherItem : PoolItem { }

    public sealed class FreshItem<TTag> : PoolItem { }

    public sealed class ColdItem<T> : MemoryObject
    {
        public override void Clear() { }
    }

    public sealed class ConstructorItem : MemoryObject
    {
        public static Action Construct;
        public ConstructorItem() { Construct?.Invoke(); }
        public override void Clear() { }
    }

    public abstract class PoolFixture
    {
        private static float s_Clock;

        private readonly Dictionary<Type, MemoryPoolBase> pools = new Dictionary<Type, MemoryPoolBase>();
        private MemoryPoolBudget savedBudget;
        protected float Now;

        [SetUp]
        public void SetUpPool()
        {
            MemoryPoolRegistry.InitializeMainThread();
            savedBudget = MemoryPoolRegistry.Budget;
            MemoryPool.SetBudget(MemoryPoolBudget.Default);
            Now = Mathf.Max(s_Clock + 100f, Time.realtimeSinceStartup);
            s_Clock = Now;
            Use<PoolItem>();
            Use<OtherItem>();
            Use<ConstructorItem>();
        }

        protected void Use<T>() where T : MemoryObject, new()
        {
            MemoryPool<T> pool = MemoryPool<T>.Instance;
            pools[typeof(T)] = pool;
            Assert.That(pool.GetInfo().LeasedCount, Is.Zero, typeof(T).Name + " has leases from an earlier test.");
            pool.Trim(0);
        }

        protected void Tick(float seconds = 0f)
        {
            Now += seconds;
            s_Clock = Now;
            MemoryPoolRegistry.TickAll(Now);
        }

        protected static MemoryPoolInfo Info<T>() where T : MemoryObject, new()
        {
            return MemoryPool<T>.Instance.GetInfo();
        }

        [TearDown]
        public void TearDownPool()
        {
            ConstructorItem.Construct = null;
            List<Exception> errors = null;
            try
            {
                foreach (KeyValuePair<Type, MemoryPoolBase> pair in pools)
                {
                    try { Assert.That(pair.Value.GetInfo().LeasedCount, Is.Zero, pair.Key.Name + " has unreturned leases."); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                    try { pair.Value.Trim(0); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
            }
            finally
            {
                pools.Clear();
                MemoryPool.SetBudget(savedBudget);
            }

            if (errors != null) throw new AggregateException(errors);
        }
    }
}
