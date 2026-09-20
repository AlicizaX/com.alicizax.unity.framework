using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;

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
        private readonly HashSet<Type> types = new HashSet<Type>();
        private int shortDecay, longDecay, zeroReserve, unschedule, autoTrim;
        private MemoryPoolPhase phase;
        protected int Frame;

        [SetUp]
        public void SetUpPool()
        {
            MemoryPoolRegistry.InitializeMainThread();
            shortDecay = MemoryPool.ShortDecayStartFrames;
            longDecay = MemoryPool.LongDecayStartFrames;
            zeroReserve = MemoryPool.ZeroFreeReserveStartFrames;
            unschedule = MemoryPool.UnscheduleIdleFrames;
            autoTrim = MemoryPool.AutoTrimNativeMetadataFrames;
            phase = MemoryPoolRegistry.Phase;
            MemoryPoolRegistry.Phase = MemoryPoolPhase.Gameplay;
            Frame = MemoryPoolRegistry.CurrentFrame + 100;
            Use<PoolItem>();
            Use<OtherItem>();
            Use<ConstructorItem>();
        }

        protected void Use<T>() where T : MemoryObject, new()
        {
            types.Add(typeof(T));
            Assert.That(Info<T>().UsingCount, Is.Zero, typeof(T).Name + " has leases from an earlier test.");
            MemoryPool<T>.ClearAll();
            MemoryPool<T>.SetCapacity(128, 512);
            MemoryPool<T>.ResetStats();
        }

        protected void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++) MemoryPoolRegistry.TickAll(++Frame);
        }

        protected static MemoryPoolInfo Info<T>() where T : MemoryObject, new()
        {
            MemoryPoolInfo info = default;
            MemoryPool<T>.GetInfo(ref info);
            return info;
        }

        [TearDown]
        public void TearDownPool()
        {
            ConstructorItem.Construct = null;
            List<Exception> errors = null;
            try
            {
                foreach (Type type in types)
                {
                    try
                    {
                        MemoryPoolInfo info = default;
                        MemoryPool.GetHandle(type).Inner.GetInfo(ref info);
                        Assert.That(info.UsingCount, Is.Zero, type.Name + " has unreturned leases.");
                    }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                    try { MemoryPool.RemoveAll(type); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
            }
            finally
            {
                types.Clear();
                MemoryPool.ShortDecayStartFrames = shortDecay;
                MemoryPool.LongDecayStartFrames = longDecay;
                MemoryPool.ZeroFreeReserveStartFrames = zeroReserve;
                MemoryPool.UnscheduleIdleFrames = unschedule;
                MemoryPool.AutoTrimNativeMetadataFrames = autoTrim;
                MemoryPoolRegistry.Phase = phase;
            }
            if (errors != null) throw new AggregateException(errors);
        }
    }
}
