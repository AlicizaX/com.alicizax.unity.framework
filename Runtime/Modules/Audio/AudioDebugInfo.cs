using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal interface IAudioDebugService
    {
        int CategoryCount { get; }
        int ClipCacheCount { get; }
        int ClipCacheCapacity { get; }
        AudioClipCacheEntry FirstClipCacheEntry { get; }

        void FillServiceDebugInfo(AudioServiceDebugInfo info);
        void FillCategoryDebugInfo(int typeIndex, AudioCategoryDebugInfo info);
        bool FillAgentDebugInfo(int typeIndex, int agentIndex, AudioAgentDebugInfo info);
        void FillClipCacheDebugInfo(AudioClipCacheEntry entry, AudioClipCacheDebugInfo info);
    }

    internal sealed class AudioServiceDebugInfo
    {
        public bool Initialized;
        public bool Enable;
        public float Volume;
        public int CategoryCount;
        public int TotalSourceCount;
        public int ActiveAgentCount;
        public int HandleCapacity;
        public int ClipCacheCount;
        public int ClipCacheCapacity;
        public float ClipCacheTtl;
        public AudioCachePolicy DefaultCachePolicy;
        public int LoadingClipCount;
        public int CachePolicyNoneCount;
        public int CachePolicyTtlCount;
        public int CachePolicyPinCount;
        public AudioListener Listener;
        public Transform InstanceRoot;
    }

    internal sealed class AudioCategoryDebugInfo
    {
        public AudioType Type;
        public bool Enabled;
        public float Volume;
        public int Capacity;
        public int CreatedCount;
        public int ActiveCount;
        public int FreeCount;
    }

    internal sealed class AudioAgentDebugInfo
    {
        public AudioType Type;
        public AudioAgentRuntimeState State;
        public int Index;
        public ulong Handle;
        public string Address;
        public AudioClip Clip;
        public bool Spatial;
        public bool Occluded;
        public float Volume;
        public float MinDistance;
        public float MaxDistance;
    }

    internal sealed class AudioClipCacheDebugInfo
    {
        public string Address;
        public AudioClip Clip;
        public int RefCount;
        public int PendingCount;
        public AudioCachePolicy CachePolicy;
        public bool Loading;
        public bool Pinned;
        public bool InLru;
        public bool IsLoaded;
        public float LastUseTime;
    }
}
