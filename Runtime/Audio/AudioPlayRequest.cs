using AlicizaX;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioPlayRequest : IMemory
    {
        public AudioType Type;
        public string Address;
        public AudioClip Clip;
        public Transform FollowTarget;
        public Vector3 Position;
        public bool UseWorldPosition;
        public bool Loop;
        public bool Async;
        public bool CacheClip;
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
            CacheClip = cacheClip;
            Spatial = false;
            SpatialBlend = 0f;
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
            CacheClip = cacheClip;
            Spatial = true;
            SpatialBlend = 1f;
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
            CacheClip = cacheClip;
            Spatial = true;
            SpatialBlend = 1f;
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

        public void Clear()
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
            CacheClip = true;
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
        }

        private void SetSpatialSettings(float minDistance, float maxDistance, AudioRolloffMode rolloffMode, float spatialBlend)
        {
            MinDistance = Mathf.Max(0f, minDistance);
            MaxDistance = Mathf.Max(MinDistance, maxDistance);
            RolloffMode = rolloffMode;
            SpatialBlend = Mathf.Clamp01(spatialBlend);
            OverrideSpatialSettings = true;
        }
    }
}
