using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal readonly struct AudioPlayRequest
    {
        public readonly AudioType Type;
        public readonly string Address;
        public readonly AudioClip Clip;
        public readonly Transform FollowTarget;
        public readonly Vector3 Position;
        public readonly bool Loop;
        public readonly bool Spatial;
        public readonly float Volume;
        public readonly AudioSpatialOptions SpatialOptions;
        public readonly AudioPlayOptions Options;

        public AudioPlayRequest(AudioType type, string address, AudioClip clip, Transform followTarget, in Vector3 position,
            bool loop, bool spatial, float volume, in AudioSpatialOptions spatialOptions, in AudioPlayOptions options)
        {
            Type = type;
            Address = address;
            Clip = clip;
            FollowTarget = followTarget;
            Position = position;
            Loop = loop;
            Spatial = spatial;
            Volume = volume;
            SpatialOptions = spatialOptions;
            Options = options;
        }
    }
}
