using System;

namespace AlicizaX.Audio.Runtime
{
    [Serializable]
    public sealed class AudioServiceConfig
    {
        public const int DefaultClipCacheCapacity = 128;
        public const float DefaultClipCacheTtl = 30f;
        public const AudioCachePolicy DefaultCachePolicy = AudioCachePolicy.Ttl;

        public int ClipCacheCapacity = DefaultClipCacheCapacity;
        public float ClipCacheTtl = DefaultClipCacheTtl;
        public AudioCachePolicy DefaultClipCachePolicy = DefaultCachePolicy;
    }
}
