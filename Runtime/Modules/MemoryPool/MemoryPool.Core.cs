using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AlicizaX
{
    internal abstract class MemoryPoolBase
    {
        internal abstract void Return(MemoryObject memory);

        internal abstract bool Tick(float now);

        internal abstract void Trim(int keepFreeCount);

        internal abstract void ApplyBudget();

        internal abstract MemoryPoolInfo GetInfo();
    }

    internal sealed class MemoryPool<T> : MemoryPoolBase where T : MemoryObject, new()
    {
        private const int InitialCapacity = 8;

        internal static readonly MemoryPool<T> Instance = new MemoryPool<T>();

        private static readonly bool s_Evictable = typeof(IPoolEvictable).IsAssignableFrom(typeof(T));

        private T[] _freeObjects = new T[InitialCapacity];
        private int _freeHead;
        private int _freeCount;
        private int _leasedCount;
        private int _createdCount;
        private int _targetFreeCount;
        private PeakWindow _peak;
        private bool _scheduled;

        private MemoryPool()
        {
            _targetFreeCount = MemoryPoolRegistry.Budget.SoftFreeCount;
            MemoryPoolRegistry.Register(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Acquire()
        {
            MemoryPoolRegistry.AssertMainThread();
            T item;
            if (_freeCount != 0)
            {
                int index = (_freeHead + --_freeCount) & (_freeObjects.Length - 1);
                item = _freeObjects[index];
                _freeObjects[index] = null;
                item.InPool = false;
            }
            else
            {
                item = new T();
                item.Pool = this;
                _createdCount++;
            }

            if (++_leasedCount > _peak.Current)
                _peak.Current = _leasedCount;
            return item;
        }

        internal override void Return(MemoryObject memory)
        {
            MemoryPoolRegistry.AssertMainThread();
            if (memory.InPool)
                ThrowAlreadyReleased();

            T item = Unsafe.As<T>(memory);
            item.Clear();
            item.InPool = true;
            _leasedCount--;
            if (_freeCount == MemoryPoolRegistry.Budget.HardFreeCount)
            {
                Evict(item);
                return;
            }

            if (_freeCount == _freeObjects.Length)
                GrowFreeObjects();

            _freeObjects[(_freeHead + _freeCount++) & (_freeObjects.Length - 1)] = item;
            RequestTick();
        }

        internal override bool Tick(float now)
        {
            MemoryPoolBudget budget = MemoryPoolRegistry.Budget;
            _peak.Advance(now, _leasedCount, budget.PeakWindowSeconds);
            _targetFreeCount = Mathf.Clamp(_peak.Peak, budget.RetainFreeCount, budget.SoftFreeCount);
            for (int trimCount = budget.TrimPerTick; trimCount != 0 && _freeCount > _targetFreeCount; trimCount--)
                EvictOldest();

            _scheduled = _freeCount > budget.RetainFreeCount;
            return _scheduled;
        }

        internal override void Trim(int keepFreeCount)
        {
            while (_freeCount > keepFreeCount)
                EvictOldest();
        }

        internal override void ApplyBudget()
        {
            MemoryPoolBudget budget = MemoryPoolRegistry.Budget;
            Trim(budget.HardFreeCount);
            if (_freeCount > budget.RetainFreeCount)
                RequestTick();
        }

        internal override MemoryPoolInfo GetInfo()
        {
            return new MemoryPoolInfo(typeof(T), _freeCount, _leasedCount, _createdCount, _targetFreeCount, _peak.Peak);
        }

        private void RequestTick()
        {
            if (_scheduled)
                return;

            _scheduled = true;
            MemoryPoolRegistry.Schedule(this);
        }

        private void EvictOldest()
        {
            T item = _freeObjects[_freeHead];
            _freeObjects[_freeHead] = null;
            _freeHead = (_freeHead + 1) & (_freeObjects.Length - 1);
            _freeCount--;
            Evict(item);
        }

        private void GrowFreeObjects()
        {
            var grown = new T[_freeObjects.Length << 1];
            for (int i = 0; i < _freeCount; i++)
                grown[i] = _freeObjects[(_freeHead + i) & (_freeObjects.Length - 1)];

            _freeObjects = grown;
            _freeHead = 0;
        }

        private static void Evict(T item)
        {
            if (s_Evictable)
                ((IPoolEvictable)item).OnEvict();
        }

        private static void ThrowAlreadyReleased()
        {
            throw new InvalidOperationException($"MemoryPool<{typeof(T).Name}>: object is already released.");
        }
    }
}
