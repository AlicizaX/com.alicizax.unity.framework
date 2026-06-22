using AlicizaX;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioPlayRequest : MemoryObject
    {
        public AudioType Type;
        public string Address;
        public AudioClip Clip;
        public Transform FollowTarget;
        public Vector3 Position;
        public bool UseWorldPosition;
        public bool Loop;
        public bool Async;
        public AudioCachePolicy CachePolicy;
        public bool Spatial;
        public float Volume;
        public float Pitch;
        public float SpatialBlend;
        public float MinDistance;
        public float MaxDistance;
        public AudioRolloffMode RolloffMode;
        public bool OverrideSpatialSettings;
        public float FadeInSeconds;
        public float FadeOutSeconds;
        public int Priority;

        public AudioPlayRequest()
        {
            Reset();
        }

        public void Set2D(AudioType type, string address, bool loop, float volume, bool async, bool cacheClip)
        {
            Reset();
            Type = type;
            Address = address;
            Loop = loop;
            Volume = volume;
            Async = async;
            CachePolicy = FromLegacyCache(cacheClip);
            Spatial = false;
            SpatialBlend = 0f;
        }

        public void Set2D(AudioType type, string address, bool loop, float volume, bool cacheClip, in AudioPlayOptions options)
        {
            Set2D(type, address, loop, volume, ResolveAsync(options), cacheClip);
            ApplyOptions(options);
        }

        public void Set2D(AudioType type, AudioClip clip, bool loop, float volume)
        {
            Reset();
            Type = type;
            Clip = clip;
            Loop = loop;
            Volume = volume;
            Spatial = false;
            SpatialBlend = 0f;
        }

        public void Set2D(AudioType type, AudioClip clip, bool loop, float volume, in AudioPlayOptions options)
        {
            Set2D(type, clip, loop, volume);
            ApplyOptions(options);
        }

        public void Set3D(AudioType type, AudioClip clip, in Vector3 position, bool loop, float volume)
        {
            Reset();
            Type = type;
            Clip = clip;
            Position = position;
            UseWorldPosition = true;
            Loop = loop;
            Volume = volume;
            Spatial = true;
            SpatialBlend = 1f;
        }

        public void Set3D(AudioType type, AudioClip clip, in Vector3 position, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            Set3D(type, clip, position, loop, volume);
            ApplySpatialOptions(spatial);
            ApplyOptions(options);
        }

        public void Set3D(AudioType type, string address, in Vector3 position, bool loop, float volume, bool async, bool cacheClip)
        {
            Reset();
            Type = type;
            Address = address;
            Position = position;
            UseWorldPosition = true;
            Loop = loop;
            Volume = volume;
            Async = async;
            CachePolicy = FromLegacyCache(cacheClip);
            Spatial = true;
            SpatialBlend = 1f;
        }

        public void Set3D(AudioType type, string address, in Vector3 position, bool loop, float volume, bool cacheClip, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            Set3D(type, address, position, loop, volume, ResolveAsync(options), cacheClip);
            ApplySpatialOptions(spatial);
            ApplyOptions(options);
        }

        public void Set3D(AudioType type, string address, in Vector3 position, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend, bool loop, float volume, bool async, bool cacheClip)
        {
            Set3D(type, address, position, loop, volume, async, cacheClip);
            SetSpatialSettings(minDistance, maxDistance, rolloffMode, spatialBlend);
        }

        public void Set3D(AudioType type, AudioClip clip, in Vector3 position, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend, bool loop, float volume)
        {
            Set3D(type, clip, position, loop, volume);
            SetSpatialSettings(minDistance, maxDistance, rolloffMode, spatialBlend);
        }

        public void SetFollow(AudioType type, string address, Transform target, in Vector3 localOffset, bool loop, float volume, bool async, bool cacheClip)
        {
            Reset();
            Type = type;
            Address = address;
            FollowTarget = target;
            Position = localOffset;
            Loop = loop;
            Volume = volume;
            Async = async;
            CachePolicy = FromLegacyCache(cacheClip);
            Spatial = true;
            SpatialBlend = 1f;
        }

        public void SetFollow(AudioType type, string address, Transform target, in Vector3 localOffset, bool loop, float volume, bool cacheClip, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            SetFollow(type, address, target, localOffset, loop, volume, ResolveAsync(options), cacheClip);
            ApplySpatialOptions(spatial);
            ApplyOptions(options);
        }

        public void SetFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop, float volume)
        {
            Reset();
            Type = type;
            Clip = clip;
            FollowTarget = target;
            Position = localOffset;
            Loop = loop;
            Volume = volume;
            Spatial = true;
            SpatialBlend = 1f;
        }

        public void SetFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, bool loop, float volume, in AudioSpatialOptions spatial, in AudioPlayOptions options)
        {
            SetFollow(type, clip, target, localOffset, loop, volume);
            ApplySpatialOptions(spatial);
            ApplyOptions(options);
        }

        public void SetFollow(AudioType type, string address, Transform target, in Vector3 localOffset, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend, bool loop, float volume, bool async, bool cacheClip)
        {
            SetFollow(type, address, target, localOffset, loop, volume, async, cacheClip);
            SetSpatialSettings(minDistance, maxDistance, rolloffMode, spatialBlend);
        }

        public void SetFollow(AudioType type, AudioClip clip, Transform target, in Vector3 localOffset, float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend, bool loop, float volume)
        {
            SetFollow(type, clip, target, localOffset, loop, volume);
            SetSpatialSettings(minDistance, maxDistance, rolloffMode, spatialBlend);
        }

        public override void Clear()
        {
            Reset();
        }

        private void Reset()
        {
            Type = AudioType.Sound;
            Address = null;
            Clip = null;
            FollowTarget = null;
            Position = Vector3.zero;
            UseWorldPosition = false;
            Loop = false;
            Async = false;
            CachePolicy = AudioCachePolicy.Default;
            Spatial = false;
            Volume = 1f;
            Pitch = 1f;
            SpatialBlend = -1f;
            MinDistance = 1f;
            MaxDistance = 500f;
            RolloffMode = AudioRolloffMode.Logarithmic;
            OverrideSpatialSettings = false;
            FadeInSeconds = 0f;
            FadeOutSeconds = 0.15f;
            Priority = 0;
        }

        private void SetSpatialSettings(float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend)
        {
            MinDistance = Mathf.Max(0f, minDistance);
            MaxDistance = Mathf.Max(MinDistance, maxDistance);
            RolloffMode = rolloffMode;
            SpatialBlend = Mathf.Clamp01(spatialBlend);
            OverrideSpatialSettings = true;
        }

        private void ApplyOptions(in AudioPlayOptions options)
        {
            if (options.Pitch > 0f)
            {
                Pitch = options.Pitch;
            }

            FadeInSeconds = Mathf.Max(0f, options.FadeInSeconds);
            if (options.FadeOutSeconds > 0f)
            {
                FadeOutSeconds = options.FadeOutSeconds;
            }

            Priority = options.Priority;
            CachePolicy = ResolveCachePolicy(options, CachePolicy);
        }

        private void ApplySpatialOptions(in AudioSpatialOptions spatial)
        {
            if (!spatial.Override)
            {
                SpatialBlend = -1f;
                OverrideSpatialSettings = false;
                return;
            }

            AudioRolloffMode rolloffMode = spatial.RolloffMode;
            SetSpatialSettings(spatial.MinDistance, spatial.MaxDistance, rolloffMode, spatial.SpatialBlend);
        }

        private static bool ResolveAsync(in AudioPlayOptions options)
        {
            return options.Async;
        }

        private static AudioCachePolicy ResolveCachePolicy(in AudioPlayOptions options, AudioCachePolicy current)
        {
            switch (options.CachePolicy)
            {
                case AudioCachePolicy.None:
                    return AudioCachePolicy.None;
                case AudioCachePolicy.Ttl:
                    return AudioCachePolicy.Ttl;
                case AudioCachePolicy.Pin:
                    return AudioCachePolicy.Pin;
                case AudioCachePolicy.Default:
                    return current;
                default:
                    return current;
            }
        }

        private static AudioCachePolicy FromLegacyCache(bool cacheClip)
        {
            return cacheClip ? AudioCachePolicy.Ttl : AudioCachePolicy.None;
        }
    }
}
