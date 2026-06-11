using System;
using System.Runtime.CompilerServices;

namespace AlicizaX.ObjectPool
{
    /// <summary>
    /// 引用类型键的开放寻址哈希表，使用身份相等性（ReferenceEquals）
    /// </summary>
    internal struct ReferenceOpenHashMap
    {
        private int[] m_Buckets;
        private object[] m_Keys;
        private int[] m_Values;
        private int[] m_Next;
        private int m_Count;
        private int m_FreeList;
        private int m_Mask;
        private int m_AllocCount;

        private const int MinCapacity = 8;

        public int Count => m_Count;

        public ReferenceOpenHashMap(int capacity)
        {
            int cap = NextPowerOf2(Math.Max(capacity, MinCapacity));
            m_Mask = cap - 1;
            m_Buckets = SlotArrayPool<int>.Rent(cap);
            m_Keys = SlotArrayPool<object>.Rent(cap);
            m_Values = SlotArrayPool<int>.Rent(cap);
            m_Next = SlotArrayPool<int>.Rent(cap);
            Array.Clear(m_Buckets, 0, m_Buckets.Length);
            Array.Clear(m_Keys, 0, m_Keys.Length);
            Array.Clear(m_Values, 0, m_Values.Length);
            Array.Clear(m_Next, 0, m_Next.Length);
            m_Count = 0;
            m_FreeList = 0;
            m_AllocCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(object key, out int value)
        {
            if (m_Buckets == null || key == null) { value = -1; return false; }
            int hash = RuntimeHelpers.GetHashCode(key) & 0x7FFFFFFF;
            int i = m_Buckets[hash & m_Mask];
            while (i > 0)
            {
                int idx = i - 1;
                if (ReferenceEquals(m_Keys[idx], key)) { value = m_Values[idx]; return true; }
                i = m_Next[idx];
            }
            value = -1;
            return false;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(object key, int value)
        {
            if (key == null) return;
            if (m_Count >= ((m_Mask + 1) * 3 >> 2))
                Grow();

            int hash = RuntimeHelpers.GetHashCode(key) & 0x7FFFFFFF;
            int bucket = hash & m_Mask;
            int i = m_Buckets[bucket];
            while (i > 0)
            {
                int ei = i - 1;
                if (ReferenceEquals(m_Keys[ei], key)) { m_Values[ei] = value; return; }
                i = m_Next[ei];
            }

            int idx;
            if (m_FreeList > 0)
            {
                idx = m_FreeList - 1;
                m_FreeList = m_Next[idx];
            }
            else
            {
                if (m_AllocCount > m_Mask) { Grow(); bucket = hash & m_Mask; }
                idx = m_AllocCount++;
            }

            m_Keys[idx] = key;
            m_Values[idx] = value;
            m_Next[idx] = m_Buckets[bucket];
            m_Buckets[bucket] = idx + 1;
            m_Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(object key)
        {
            if (m_Buckets == null || key == null) return false;
            int hash = RuntimeHelpers.GetHashCode(key) & 0x7FFFFFFF;
            int bucket = hash & m_Mask;
            int prev = 0;
            int i = m_Buckets[bucket];
            while (i > 0)
            {
                int idx = i - 1;
                if (ReferenceEquals(m_Keys[idx], key))
                {
                    if (prev == 0) m_Buckets[bucket] = m_Next[idx];
                    else m_Next[prev - 1] = m_Next[idx];
                    m_Keys[idx] = null;
                    m_Values[idx] = -1;
                    m_Next[idx] = m_FreeList;
                    m_FreeList = idx + 1;
                    m_Count--;
                    return true;
                }
                prev = i;
                i = m_Next[idx];
            }
            return false;
        }
        public void Clear()
        {
            if (m_Buckets == null) return;
            int cap = m_Mask + 1;
            Array.Clear(m_Buckets, 0, cap);
            Array.Clear(m_Keys, 0, cap);
            Array.Clear(m_Values, 0, cap);
            Array.Clear(m_Next, 0, cap);
            m_Count = 0;
            m_FreeList = 0;
            m_AllocCount = 0;
        }

        private void Grow()
        {
            int newCap = (m_Mask + 1) << 1;
            if (newCap < MinCapacity) newCap = MinCapacity;
            int newMask = newCap - 1;
            var newBuckets = SlotArrayPool<int>.Rent(newCap);
            var newKeys = SlotArrayPool<object>.Rent(newCap);
            var newValues = SlotArrayPool<int>.Rent(newCap);
            var newNext = SlotArrayPool<int>.Rent(newCap);
            Array.Clear(newBuckets, 0, newBuckets.Length);
            Array.Clear(newKeys, 0, newKeys.Length);
            Array.Clear(newValues, 0, newValues.Length);
            Array.Clear(newNext, 0, newNext.Length);

            int newAlloc = 0;
            int oldCap = m_Mask + 1;
            for (int b = 0; b < oldCap; b++)
            {
                int i = m_Buckets[b];
                while (i > 0)
                {
                    int old = i - 1;
                    int ni = newAlloc++;
                    newKeys[ni] = m_Keys[old];
                    newValues[ni] = m_Values[old];
                    int hash = RuntimeHelpers.GetHashCode(newKeys[ni]) & 0x7FFFFFFF;
                    int nb = hash & newMask;
                    newNext[ni] = newBuckets[nb];
                    newBuckets[nb] = ni + 1;
                    i = m_Next[old];
                }
            }

            SlotArrayPool<int>.Return(m_Buckets, true);
            SlotArrayPool<object>.Return(m_Keys, true);
            SlotArrayPool<int>.Return(m_Values, true);
            SlotArrayPool<int>.Return(m_Next, true);

            m_Buckets = newBuckets;
            m_Keys = newKeys;
            m_Values = newValues;
            m_Next = newNext;
            m_Mask = newMask;
            m_AllocCount = newAlloc;
            m_FreeList = 0;
        }

        public void Dispose()
        {
            SlotArrayPool<int>.Return(m_Buckets, true);
            SlotArrayPool<object>.Return(m_Keys, true);
            SlotArrayPool<int>.Return(m_Values, true);
            SlotArrayPool<int>.Return(m_Next, true);
            m_Buckets = null;
            m_Keys = null;
            m_Values = null;
            m_Next = null;
            m_Count = 0;
            m_FreeList = 0;
            m_Mask = 0;
            m_AllocCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOf2(int v)
        {
            v--;
            v |= v >> 1; v |= v >> 2; v |= v >> 4;
            v |= v >> 8; v |= v >> 16;
            return v + 1;
        }
    }
}
