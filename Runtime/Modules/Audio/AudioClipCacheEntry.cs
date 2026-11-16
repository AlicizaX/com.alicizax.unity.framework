using System.Threading;
using AlicizaX;
using AlicizaX.Resource.Runtime;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioClipCacheEntry : MemoryObject, IPoolEvictable
    {
        public ulong Version;
        // 池化条目跨多次加载复用；仅在加载中的条目被清理或条目从 MemoryPool 淘汰时释放。
        // 其数量受 clip 缓存容量限制。
        public CancellationTokenSource Cancellation;
        public string Address;
        public AssetLease<AudioClip> Lease;
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

        public void Initialize(string address, int addressHash, AudioCachePolicy cachePolicy, int slotIndex)
        {
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

        public void RemovePending(AudioLoadRequest request)
        {
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
            info.Address = Address;
            info.Clip = Clip;
            info.RefCount = RefCount;
            info.PendingCount = CountPending();
            info.CachePolicy = CachePolicy;
            info.Loading = Loading;
            info.Pinned = Pinned;
            info.InLru = InLru;
            info.IsLoaded = IsLoaded;
            info.LastUseTime = LastUseTime;
        }

        public void OnEvict()
        {
            Cancellation?.Dispose();
            Cancellation = null;
        }

        public override void Clear()
        {
            Version++;
            if (Loading)
            {
                Cancellation.Cancel();
                OnEvict();
                Loading = false;
            }

            Lease.Dispose();
            Lease = default;
            Address = null;
            Clip = null;

            AudioLoadRequest request = PendingHead;
            PendingHead = null;
            PendingTail = null;
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                request.Entry = null;
                request.Prev = null;
                request.Next = null;
                request.Agent?.OnClipLoadFailed();
                MemoryPool.Release(request);
                request = next;
            }
        }
    }
}
