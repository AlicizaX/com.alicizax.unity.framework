using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal interface IAudioDebugService
    {
        int CategoryCount { get; }
        int ClipCacheCount { get; }
        int ClipCacheCapacity { get; }
        int HandleCapacity { get; }
        bool Initialized { get; }
        bool UnityAudioDisabled { get; }
        AudioClipCacheEntry FirstClipCacheEntry { get; }

        void FillServiceDebugInfo(AudioServiceDebugInfo info);
        bool FillCategoryDebugInfo(int typeIndex, AudioCategoryDebugInfo info);
        bool FillAgentDebugInfo(int typeIndex, int agentIndex, AudioAgentDebugInfo info);
        bool FillClipCacheDebugInfo(AudioClipCacheEntry entry, AudioClipCacheDebugInfo info);
    }

    internal sealed class AudioServiceDebugInfo
    {
        public bool Initialized;
        public bool UnityAudioDisabled;
        public bool Enable;
        public float Volume;
        public int CategoryCount;
        public int ActiveAgentCount;
        public int HandleCapacity;
        public int ClipCacheCount;
        public int ClipCacheCapacity;
        public AudioListener Listener;
        public Transform InstanceRoot;

        public void Clear()
        {
            Initialized = false;
            UnityAudioDisabled = false;
            Enable = false;
            Volume = 0f;
            CategoryCount = 0;
            ActiveAgentCount = 0;
            HandleCapacity = 0;
            ClipCacheCount = 0;
            ClipCacheCapacity = 0;
            Listener = null;
            InstanceRoot = null;
        }
    }

    internal sealed class AudioCategoryDebugInfo
    {
        public AudioType Type;
        public bool Enabled;
        public float Volume;
        public int Capacity;
        public int ActiveCount;
        public int FreeCount;
        public int HeapCount;

        public void Clear()
        {
            Type = AudioType.Sound;
            Enabled = false;
            Volume = 0f;
            Capacity = 0;
            ActiveCount = 0;
            FreeCount = 0;
            HeapCount = 0;
        }
    }

    internal sealed class AudioAgentDebugInfo
    {
        public AudioType Type;
        public AudioAgentRuntimeState State;
        public int Index;
        public int GlobalIndex;
        public int ActiveIndex;
        public ulong Handle;
        public string Address;
        public AudioClip Clip;
        public Transform FollowTarget;
        public Vector3 Position;
        public bool Loop;
        public bool Spatial;
        public bool Occluded;
        public float Volume;
        public float Pitch;
        public float SpatialBlend;
        public float MinDistance;
        public float MaxDistance;
        public float StartedAt;

        public void Clear()
        {
            Type = AudioType.Sound;
            State = AudioAgentRuntimeState.Free;
            Index = 0;
            GlobalIndex = 0;
            ActiveIndex = -1;
            Handle = 0UL;
            Address = null;
            Clip = null;
            FollowTarget = null;
            Position = Vector3.zero;
            Loop = false;
            Spatial = false;
            Occluded = false;
            Volume = 0f;
            Pitch = 0f;
            SpatialBlend = 0f;
            MinDistance = 0f;
            MaxDistance = 0f;
            StartedAt = 0f;
        }
    }

    internal sealed class AudioClipCacheDebugInfo
    {
        public string Address;
        public AudioClip Clip;
        public int RefCount;
        public int PendingCount;
        public bool Loading;
        public bool Pinned;
        public bool CacheAfterUse;
        public bool InLru;
        public bool IsLoaded;
        public bool HasValidHandle;
        public float LastUseTime;

        public void Clear()
        {
            Address = null;
            Clip = null;
            RefCount = 0;
            PendingCount = 0;
            Loading = false;
            Pinned = false;
            CacheAfterUse = false;
            InLru = false;
            IsLoaded = false;
            HasValidHandle = false;
            LastUseTime = 0f;
        }
    }
}
