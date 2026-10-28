using System;

namespace AlicizaX.Audio.Runtime
{
    /// <summary>
    /// 负责 <see cref="AudioClipCacheEntry"/> 的槽位存储、地址哈希以及 LRU/全量双向链表。
    /// 缓存策略、加载和引用计数仍留在 <see cref="AudioService"/>；本类型只维护侵入式表，
    /// 使槽位、哈希和链表的不变量集中在一处。
    /// </summary>
    internal sealed class AudioClipCacheTable
    {
        private int[] _buckets = Array.Empty<int>();
        private AudioClipCacheEntry[] _entries = Array.Empty<AudioClipCacheEntry>();
        private int[] _freeSlots = Array.Empty<int>();
        private int _bucketMask;
        private int _freeCount;
        private int _count;
        private int _capacity;
        private AudioClipCacheEntry _lruHead;
        private AudioClipCacheEntry _lruTail;
        private AudioClipCacheEntry _allHead;
        private AudioClipCacheEntry _allTail;

        internal int Count => _count;

        internal int Capacity => _capacity;

        internal AudioClipCacheEntry LruHead => _lruHead;

        internal AudioClipCacheEntry LruTail => _lruTail;

        internal AudioClipCacheEntry AllHead => _allHead;

        internal AudioClipCacheEntry AllTail => _allTail;

        internal AudioClipCacheEntry[] Entries => _entries;

        internal int[] Buckets => _buckets;

        internal int[] FreeSlots => _freeSlots;

        internal int FreeCount => _freeCount;

        internal void Initialize(int capacity)
        {
            _capacity = capacity;
            int bucketCount = NextPowerOfTwo(capacity << 1);
            if (bucketCount < 16)
                bucketCount = 16;

            _buckets = new int[bucketCount];
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = -1;

            _entries = new AudioClipCacheEntry[capacity];
            _freeSlots = new int[capacity];
            for (int i = 0; i < _freeSlots.Length; i++)
                _freeSlots[i] = _freeSlots.Length - 1 - i;

            _freeCount = _freeSlots.Length;
            _bucketMask = bucketCount - 1;
            _count = 0;
            _lruHead = null;
            _lruTail = null;
            _allHead = null;
            _allTail = null;
        }

        internal void Clear()
        {
            _buckets = Array.Empty<int>();
            _entries = Array.Empty<AudioClipCacheEntry>();
            _freeSlots = Array.Empty<int>();
            _freeCount = 0;
            _count = 0;
            _capacity = 0;
            _lruHead = null;
            _lruTail = null;
            _allHead = null;
            _allTail = null;
        }

        internal bool TryGet(string address, int hash, out AudioClipCacheEntry entry)
        {
            if (_buckets.Length == 0)
            {
                entry = null;
                return false;
            }

            int slotIndex = _buckets[hash & _bucketMask];
            while (slotIndex >= 0)
            {
                AudioClipCacheEntry current = _entries[slotIndex];
                if (current != null && current.AddressHash == hash && string.Equals(current.Address, address, StringComparison.Ordinal))
                {
                    entry = current;
                    return true;
                }

                slotIndex = current?.HashNextIndex ?? -1;
            }

            entry = null;
            return false;
        }

        internal void Add(AudioClipCacheEntry entry)
        {
            int slotIndex = entry.SlotIndex;
            _entries[slotIndex] = entry;
            int bucket = entry.AddressHash & _bucketMask;
            entry.HashNextIndex = _buckets[bucket];
            _buckets[bucket] = slotIndex;
            _count++;
        }

        internal void Remove(AudioClipCacheEntry entry)
        {
            int bucket = entry.AddressHash & _bucketMask;
            int currentIndex = _buckets[bucket];
            int previousIndex = -1;
            while (currentIndex >= 0)
            {
                AudioClipCacheEntry current = _entries[currentIndex];
                if (ReferenceEquals(current, entry))
                {
                    if (previousIndex < 0)
                        _buckets[bucket] = current.HashNextIndex;
                    else
                        _entries[previousIndex].HashNextIndex = current.HashNextIndex;

                    current.HashNextIndex = -1;
                    _count--;
                    return;
                }

                previousIndex = currentIndex;
                currentIndex = current?.HashNextIndex ?? -1;
            }
        }

        internal int AcquireSlot()
        {
            return _freeCount > 0 ? _freeSlots[--_freeCount] : -1;
        }

        internal void ReleaseSlot(AudioClipCacheEntry entry)
        {
            int slotIndex = entry.SlotIndex;
            _entries[slotIndex] = null;
            _freeSlots[_freeCount++] = slotIndex;
            entry.SlotIndex = -1;
        }

        internal void AddToLruTail(AudioClipCacheEntry entry)
        {
            entry.InLru = true;
            entry.LruPrev = _lruTail;
            entry.LruNext = null;
            if (_lruTail != null)
                _lruTail.LruNext = entry;
            else
                _lruHead = entry;

            _lruTail = entry;
        }

        internal void RemoveFromLru(AudioClipCacheEntry entry)
        {
            if (!entry.InLru)
                return;

            AudioClipCacheEntry prev = entry.LruPrev;
            AudioClipCacheEntry next = entry.LruNext;
            if (prev != null)
                prev.LruNext = next;
            else
                _lruHead = next;

            if (next != null)
                next.LruPrev = prev;
            else
                _lruTail = prev;

            entry.LruPrev = null;
            entry.LruNext = null;
            entry.InLru = false;
        }

        internal void MoveLruToTail(AudioClipCacheEntry entry)
        {
            if (!entry.InLru || ReferenceEquals(_lruTail, entry))
                return;

            RemoveFromLru(entry);
            AddToLruTail(entry);
        }

        internal void AddToAllList(AudioClipCacheEntry entry)
        {
            entry.AllPrev = _allTail;
            entry.AllNext = null;
            if (_allTail != null)
                _allTail.AllNext = entry;
            else
                _allHead = entry;

            _allTail = entry;
        }

        internal void RemoveFromAllList(AudioClipCacheEntry entry)
        {
            AudioClipCacheEntry prev = entry.AllPrev;
            AudioClipCacheEntry next = entry.AllNext;
            if (prev != null)
                prev.AllNext = next;
            else
                _allHead = next;

            if (next != null)
                next.AllPrev = prev;
            else
                _allTail = prev;

            entry.AllPrev = null;
            entry.AllNext = null;
        }

        internal static int Hash(string address)
        {
            unchecked
            {
                int hash = 5381;
                for (int i = 0; i < address.Length; i++)
                    hash = ((hash << 5) + hash) ^ address[i];

                return hash & 0x7fffffff;
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }
    }
}
