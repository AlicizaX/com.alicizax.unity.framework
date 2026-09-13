using System.Threading;
using AlicizaX;
using AlicizaX.Resource.Runtime;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioClipCacheEntry : MemoryObject, IPoolEvictable
    {
        public AudioService Owner;
        public ulong Version;
        public CancellationTokenSource Cancellation;
        public string Address;
        public ResourceAssetLease<AudioClip> Lease;
        public AudioClip Clip;
        public AudioLoadRequest PendingHead;
        public AudioLoadRequest PendingTail;
        public AudioClipCacheEntry LruPrev;
        public AudioClipCacheEntry LruNext;
        public AudioClipCacheEntry AllPrev;
        public AudioClipCacheEntry AllNext;
        public int SlotIndex;
        public int HashNextIndex;
        public int RefCount;
        public int AddressHash;
        public AudioCachePolicy CachePolicy;
        public bool Loading;
        public bool Pinned => CachePolicy == AudioCachePolicy.Pin;
        public bool CacheAfterUse => CachePolicy is AudioCachePolicy.Ttl or AudioCachePolicy.Pin;
        public bool InLru;
        public float LastUseTime;

        public bool IsLoaded => Clip != null && Lease.IsValid && !Loading;

        public void Initialize(AudioService owner, string address, int addressHash, AudioCachePolicy cachePolicy, int slotIndex)
        {
            Version++;
            Owner = owner;
            Address = address;
            AddressHash = addressHash;
            SlotIndex = slotIndex;
            HashNextIndex = -1;
            CachePolicy = cachePolicy;
            LastUseTime = Time.realtimeSinceStartup;
        }

        public void AddPending(AudioLoadRequest request)
        {
            request.Entry = this;
            request.Prev = PendingTail;
            request.Next = null;
            if (PendingTail == null)
            {
                PendingHead = request;
                PendingTail = request;
                return;
            }

            PendingTail.Next = request;
            PendingTail = request;
        }

        public bool RemovePending(AudioLoadRequest request)
        {
            if (request == null || !ReferenceEquals(request.Entry, this))
            {
                return false;
            }

            AudioLoadRequest prev = request.Prev;
            AudioLoadRequest next = request.Next;
            if (prev != null)
            {
                prev.Next = next;
            }
            else
            {
                PendingHead = next;
            }

            if (next != null)
            {
                next.Prev = prev;
            }
            else
            {
                PendingTail = prev;
            }

            request.Entry = null;
            request.Prev = null;
            request.Next = null;
            return true;
        }

        public int CountPending()
        {
            int count = 0;
            AudioLoadRequest request = PendingHead;
            while (request != null)
            {
                count++;
                request = request.Next;
            }

            return count;
        }

        public void FillDebugInfo(AudioClipCacheDebugInfo info)
        {
            if (info == null)
            {
                return;
            }

            info.Address = Address;
            info.Clip = Clip;
            info.RefCount = RefCount;
            info.PendingCount = CountPending();
            info.CachePolicy = CachePolicy;
            info.Loading = Loading;
            info.Pinned = Pinned;
            info.CacheAfterUse = CacheAfterUse;
            info.InLru = InLru;
            info.IsLoaded = IsLoaded;
            info.HasValidHandle = Lease.IsValid;
            info.LastUseTime = LastUseTime;
        }

        public void OnEvict()
        {
            Cancellation?.Dispose();
            Cancellation = null;
        }

        public override void Clear()
        {
            Owner = null;
            if (Loading)
            {
                Cancellation?.Cancel();
                OnEvict();
            }

            Lease.Dispose();

            AudioLoadRequest request = PendingHead;
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                MemoryPool.Release(request);
                request = next;
            }

            Address = null;
            Lease = default;
            Clip = null;
            PendingHead = null;
            PendingTail = null;
            LruPrev = null;
            LruNext = null;
            AllPrev = null;
            AllNext = null;
            SlotIndex = -1;
            HashNextIndex = -1;
            RefCount = 0;
            AddressHash = 0;
            CachePolicy = AudioCachePolicy.Default;
            Loading = false;
            InLru = false;
            LastUseTime = 0f;
        }
    }
}
