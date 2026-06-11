using System;
using UnityEngine;
using UnityEngine.Audio;

namespace AlicizaX.Audio.Runtime
{
    [Serializable]
    public sealed class AudioGroupConfig
    {
        [SerializeField] private string m_Name = null;
        [SerializeField] private AudioMixerGroup m_MixerGroup = null;
        [SerializeField] private bool m_Mute = false;
        [SerializeField, Range(0f, 1f)] private float m_Volume = 1f;
        [SerializeField, Min(1)] private int m_AgentHelperCount = 8;
        [SerializeField] private string m_ExposedVolumeParameter = null;
        [SerializeField, Range(0f, 1f)] private float m_SpatialBlend = 1f;
        [SerializeField, Range(0f, 5f)] private float m_DopplerLevel = 1f;
        [SerializeField, Range(0f, 360f)] private float m_Spread = 0f;
        [SerializeField, Range(0, 256)] private int m_SourcePriority = 128;
        [SerializeField, Range(0f, 1.1f)] private float m_ReverbZoneMix = 1f;
        [SerializeField] private bool m_OcclusionEnabled = false;
        [SerializeField] private LayerMask m_OcclusionMask = ~0;
        [SerializeField, Min(0.02f)] private float m_OcclusionCheckInterval = 0.12f;
        [SerializeField, Range(500f, 22000f)] private float m_OcclusionLowPassCutoff = 1200f;
        [SerializeField, Range(0f, 1f)] private float m_OcclusionVolumeMultiplier = 0.55f;

        public AudioType AudioType;
        public AudioRolloffMode audioRolloffMode = AudioRolloffMode.Logarithmic;
        public float minDistance = 1f;
        public float maxDistance = 500f;

        public string Name => m_Name;
        public AudioMixerGroup MixerGroup => m_MixerGroup;
        public bool Mute => m_Mute;
        public float Volume => m_Volume;
        public int AgentHelperCount => m_AgentHelperCount > 0 ? m_AgentHelperCount : 1;
        public string ExposedVolumeParameter => m_ExposedVolumeParameter;
        public float SpatialBlend => m_SpatialBlend;
        public float DopplerLevel => m_DopplerLevel;
        public float Spread => m_Spread;
        public int SourcePriority => m_SourcePriority;
        public float ReverbZoneMix => m_ReverbZoneMix;
        public bool OcclusionEnabled => m_OcclusionEnabled;
        public LayerMask OcclusionMask => m_OcclusionMask;
        public float OcclusionCheckInterval => m_OcclusionCheckInterval;
        public float OcclusionLowPassCutoff => m_OcclusionLowPassCutoff;
        public float OcclusionVolumeMultiplier => m_OcclusionVolumeMultiplier;
        public AudioRolloffMode RolloffMode => audioRolloffMode;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;

        internal void SetDefaults(AudioType type, string name, string exposedVolumeParameter, int agentHelperCount, float spatialBlend, bool occlusionEnabled)
        {
            AudioType = type;
            m_Name = name;
            m_Mute = false;
            m_Volume = 1f;
            m_AgentHelperCount = agentHelperCount;
            m_ExposedVolumeParameter = exposedVolumeParameter;
            m_SpatialBlend = spatialBlend;
            m_DopplerLevel = 1f;
            m_Spread = 0f;
            m_SourcePriority = type == AudioType.Music ? 32 : 128;
            m_ReverbZoneMix = 1f;
            m_OcclusionEnabled = occlusionEnabled;
            m_OcclusionMask = ~0;
            m_OcclusionCheckInterval = 0.12f;
            m_OcclusionLowPassCutoff = 1200f;
            m_OcclusionVolumeMultiplier = 0.55f;
            audioRolloffMode = AudioRolloffMode.Logarithmic;
            minDistance = type == AudioType.Music || type == AudioType.UISound ? 1f : 2f;
            maxDistance = type == AudioType.Music || type == AudioType.UISound ? 25f : 80f;
        }
    }
}
