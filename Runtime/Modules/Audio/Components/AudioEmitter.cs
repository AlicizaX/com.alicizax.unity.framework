using AlicizaX;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Audio/Audio Emitter")]
    public sealed class AudioEmitter : MonoBehaviour
    {
        private const int GizmoCircleSegments = 96;
        private const int GizmoLatitudeCount = 4;

        private enum AudioEmitterClipMode
        {
            Address = 0,
            Clip = 1
        }

        [Header("Playback")]
        [SerializeField] private AudioType m_AudioType = AudioType.Ambient;
        [SerializeField] private AudioEmitterClipMode m_ClipMode = AudioEmitterClipMode.Address;
        [SerializeField] private string m_Address = string.Empty;
        [SerializeField] private AudioClip m_Clip;
        [SerializeField] private bool m_PlayOnEnable = true;
        [SerializeField] private bool m_Loop = true;
        [SerializeField, Range(0f, 1f)] private float m_Volume = 1f;
        [SerializeField] private bool m_Async = true;
        [SerializeField, HideInInspector] private bool m_CacheClip = true;
        [SerializeField, HideInInspector] private bool m_CachePolicyMigrated;
        [SerializeField] private AudioCachePolicy m_CachePolicy = AudioCachePolicy.Ttl;
        [SerializeField] private bool m_StopWithFadeout = true;

        [Header("Spatial")]
        [SerializeField] private bool m_FollowSelf = true;
        [SerializeField] private Vector3 m_FollowOffset = Vector3.zero;
        [SerializeField, Range(0f, 1f)] private float m_SpatialBlend = 1f;
        [SerializeField] private AudioRolloffMode m_RolloffMode = AudioRolloffMode.Logarithmic;
        [SerializeField, Min(0f)] private float m_MinDistance = 2f;
        [SerializeField, Min(0f)] private float m_MaxDistance = 30f;

        [Header("Trigger")]
        [SerializeField] private bool m_UseTriggerRange = false;
        [SerializeField, Min(0f)] private float m_TriggerRange = 10f;
        [SerializeField, Min(0f)] private float m_TriggerHysteresis = 0.5f;

        [Header("Gizmos")]
        [SerializeField] private bool m_DrawGizmos = true;
        [SerializeField] private bool m_DrawOnlyWhenSelected = true;
        [SerializeField] private Color m_TriggerColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [SerializeField] private Color m_MinDistanceColor = new Color(1f, 0.9f, 0.2f, 0.9f);
        [SerializeField] private Color m_MaxDistanceColor = new Color(1f, 0.45f, 0.05f, 0.9f);

        private AudioService _audioService;
        private Transform _cachedTransform;
        private ulong _handle;
        private bool _isPlaying;
        private bool _insideTriggerRange;

        public ulong Handle => _handle;
        public bool IsPlaying => _isPlaying;

        private void Awake()
        {
            EnsureCachePolicyMigrated();
            _cachedTransform = transform;
        }

        private void OnEnable()
        {
            TryBindService();

            if (m_PlayOnEnable && !m_UseTriggerRange)
            {
                StartPlayback();
            }
        }

        private void Update()
        {
            if (!m_UseTriggerRange)
            {
                return;
            }

            if (_audioService == null)
            {
                if (!TryBindService())
                {
                    return;
                }
            }

            if (!_isPlaying && !_insideTriggerRange && !HasPlayableAsset())
            {
                return;
            }

            Transform listener = _audioService.ListenerTransform;
            if (listener == null)
            {
                _insideTriggerRange = false;
                StopPlayback();
                return;
            }

            Vector3 offset = listener.position - _cachedTransform.position;
            float range = _isPlaying ? m_TriggerRange + m_TriggerHysteresis : m_TriggerRange;
            float sqrRange = range * range;

            if (offset.sqrMagnitude <= sqrRange)
            {
                RefreshPlaybackState();
                if (!_insideTriggerRange || (m_Loop && !_isPlaying))
                {
                    _insideTriggerRange = true;
                    StartPlayback();
                }
            }
            else
            {
                _insideTriggerRange = false;
                StopPlayback();
            }
        }

        private void OnDisable()
        {
            StopPlayback();
            _audioService = null;
            _insideTriggerRange = false;
        }

        public void Play()
        {
            if (_audioService == null)
            {
                TryBindService();
            }

            StartPlayback();
        }

        public void Stop()
        {
            StopPlayback();
        }

        private bool TryBindService()
        {
            if (AppServices.HasWorld && AppServices.App.TryGet(out _audioService))
            {
                return true;
            }

            return false;
        }

        private void StartPlayback()
        {
            if (_audioService == null || _isPlaying || !HasPlayableAsset())
            {
                return;
            }

            float maxDistance = m_MaxDistance >= m_MinDistance ? m_MaxDistance : m_MinDistance;
            AudioSpatialOptions spatial = new AudioSpatialOptions
            {
                Override = true,
                SpatialBlend = m_SpatialBlend,
                MinDistance = m_MinDistance,
                MaxDistance = maxDistance,
                RolloffMode = m_RolloffMode
            };
            AudioPlayOptions options = new AudioPlayOptions
            {
                Async = m_Async,
                CachePolicy = m_CachePolicy
            };

            if (m_FollowSelf)
            {
                _handle = m_ClipMode == AudioEmitterClipMode.Clip
                    ? _audioService.PlayFollow(
                        m_AudioType,
                        m_Clip,
                        _cachedTransform,
                        m_FollowOffset,
                        m_Loop,
                        m_Volume,
                        spatial,
                        options)
                    : _audioService.PlayFollow(
                        m_AudioType,
                        m_Address,
                        _cachedTransform,
                        m_FollowOffset,
                        m_Loop,
                        m_Volume,
                        spatial,
                        options);
            }
            else
            {
                Vector3 position = transform.position;
                if (_cachedTransform != null)
                {
                    position = _cachedTransform.position;
                }

                _handle = m_ClipMode == AudioEmitterClipMode.Clip
                    ? _audioService.Play3D(
                        m_AudioType,
                        m_Clip,
                        position,
                        m_Loop,
                        m_Volume,
                        spatial,
                        options)
                    : _audioService.Play3D(
                        m_AudioType,
                        m_Address,
                        position,
                        m_Loop,
                        m_Volume,
                        spatial,
                        options);
            }

            _isPlaying = _handle != 0UL;
        }

        private void StopPlayback()
        {
            if (!_isPlaying)
            {
                _handle = 0UL;
                return;
            }

            if (_audioService != null && _handle != 0UL)
            {
                _audioService.Stop(_handle, m_StopWithFadeout);
            }

            _handle = 0UL;
            _isPlaying = false;
        }

        private void EnsureCachePolicyMigrated()
        {
            if (m_CachePolicyMigrated)
            {
                return;
            }

            m_CachePolicy = m_CacheClip ? AudioCachePolicy.Ttl : AudioCachePolicy.None;
            m_CachePolicyMigrated = true;
        }

        private bool HasPlayableAsset()
        {
            return m_ClipMode == AudioEmitterClipMode.Clip
                ? m_Clip != null
                : !string.IsNullOrEmpty(m_Address);
        }

        private void RefreshPlaybackState()
        {
            if (!_isPlaying || _audioService == null || _handle == 0UL)
            {
                return;
            }

            if (_audioService.IsPlaying(_handle))
            {
                return;
            }

            _handle = 0UL;
            _isPlaying = false;
        }

        private void OnValidate()
        {
            EnsureCachePolicyMigrated();
            if (m_MaxDistance < m_MinDistance)
            {
                m_MaxDistance = m_MinDistance;
            }
        }

        private void OnDrawGizmos()
        {
            if (!m_DrawOnlyWhenSelected)
            {
                DrawEmitterGizmos();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (m_DrawOnlyWhenSelected)
            {
                DrawEmitterGizmos();
            }
        }

        private void DrawEmitterGizmos()
        {
            if (!m_DrawGizmos)
            {
                return;
            }

            Vector3 position = transform.position;
            if (_cachedTransform != null)
            {
                position = _cachedTransform.position;
            }

            if (m_UseTriggerRange)
            {
                DrawDistanceSphere(position, m_TriggerRange, m_TriggerColor);
            }

            DrawDistanceSphere(position, m_MinDistance, m_MinDistanceColor);
            DrawDistanceSphere(position, m_MaxDistance, m_MaxDistanceColor);
        }

        private static void DrawDistanceSphere(in Vector3 center, float radius, Color color)
        {
            if (radius <= 0f)
            {
                return;
            }

            Color previousColor = Gizmos.color;
            Gizmos.color = color;
            DrawCircle(center, Vector3.right, Vector3.forward, radius);
            DrawCircle(center, Vector3.right, Vector3.up, radius);
            DrawCircle(center, Vector3.forward, Vector3.up, radius);

            Color guideColor = color;
            guideColor.a *= 0.45f;
            Gizmos.color = guideColor;
            for (int i = 1; i <= GizmoLatitudeCount; i++)
            {
                float normalized = i / (float)(GizmoLatitudeCount + 1);
                float angle = normalized * Mathf.PI * 0.5f;
                float y = Mathf.Sin(angle) * radius;
                float ringRadius = Mathf.Cos(angle) * radius;

                DrawCircle(center + Vector3.up * y, Vector3.right, Vector3.forward, ringRadius);
                DrawCircle(center - Vector3.up * y, Vector3.right, Vector3.forward, ringRadius);
                DrawCircle(center + Vector3.right * y, Vector3.forward, Vector3.up, ringRadius);
                DrawCircle(center - Vector3.right * y, Vector3.forward, Vector3.up, ringRadius);
            }

            Gizmos.color = previousColor;
        }

        private static void DrawCircle(in Vector3 center, in Vector3 axisA, in Vector3 axisB, float radius)
        {
            Vector3 previous = center + axisA * radius;
            for (int i = 1; i <= GizmoCircleSegments; i++)
            {
                float radians = i * (Mathf.PI * 2f / GizmoCircleSegments);
                Vector3 next = center + (axisA * Mathf.Cos(radians) + axisB * Mathf.Sin(radians)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
