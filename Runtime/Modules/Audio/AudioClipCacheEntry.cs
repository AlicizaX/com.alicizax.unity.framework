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
        // 池化条目跨多次加载复用；仅在加载中的条目被清理或条目从 MemoryPool 淘汰时释放。
        // 其数量受 clip 缓存容量限制。
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
                try
                {
                    Cancellation?.Cancel();
                }
                catch (System.Exception exception)
                {
                    // 用户的取消回调不能阻塞缓存清理。
                    AlicizaX.Log.Exception(exception);
                }

                OnEvict();
            }

            Lease.Dispose();

            // 取消加载可能同步回调 OnClipLoadCompleted；此时 Owner 已置空，回调只会丢弃租约。
            // 之后再摘除 pending 链表并通知 agent，确保每个请求只被处理一次。
            AudioLoadRequest request = PendingHead;
            PendingHead = null;
            PendingTail = null;
            while (request != null)
            {
                AudioLoadRequest next = request.Next;
                request.Entry = null;
                request.Prev = null;
                request.Next = null;
                request.Agent?.OnClipLoadFailed(request.Generation);
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
