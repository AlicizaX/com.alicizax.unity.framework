using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    public struct AudioSpatialOptions
    {
        public bool Override;
        public float SpatialBlend;
        public float MinDistance;
        public float MaxDistance;
        public AudioRolloffMode RolloffMode;

        public static AudioSpatialOptions Default => default;
    }
}
