using AlicizaX;
using UnityEngine;
using UnityEngine.Audio;

namespace AlicizaX.Audio.Runtime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Game Framework/Audio")]
    [DefaultExecutionOrder(-400)]
    public sealed class AudioComponent : MonoBehaviour
    {
        [SerializeField] private AudioMixer m_AudioMixer;
        [SerializeField] private AudioListener m_AudioListener;
        [SerializeField] private AudioGroupConfig[] m_AudioGroupConfigs;

        private AudioService _audioService;

        private void Awake()
        {
            _audioService = new AudioService();
            if (m_AudioMixer == null)
            {
                throw new GameFrameworkException("AudioMixer is not assigned. Please assign an AudioMixer in the inspector.");
            }

            if (m_AudioListener == null)
            {
                throw new GameFrameworkException("AudioListener is not assigned. Please assign an AudioListener in the inspector.");
            }

            _audioService.Initialize(m_AudioGroupConfigs, m_AudioListener, transform, m_AudioMixer);
            AppServices.RegisterApp<IAudioService>(_audioService);
        }


#if UNITY_EDITOR
        private void Reset()
        {
            ResetGroupConfigsForEditor();
        }

        private void OnValidate()
        {
            EnsureGroupConfigsForEditor();
        }

        internal void ResetGroupConfigsForEditor()
        {
            m_AudioGroupConfigs = CreateDefaultConfigsForEditor();
        }

        internal void EnsureGroupConfigsForEditor()
        {
            int count = (int)AudioType.Max;
            if (m_AudioGroupConfigs == null || m_AudioGroupConfigs.Length == 0)
            {
                m_AudioGroupConfigs = CreateDefaultConfigsForEditor();
                return;
            }

            AudioGroupConfig[] configs = new AudioGroupConfig[count];
            for (int i = 0; i < m_AudioGroupConfigs.Length; i++)
            {
                AudioGroupConfig config = m_AudioGroupConfigs[i];
                if (config == null)
                {
                    continue;
                }

                int index = (int)config.AudioType;
                if ((uint)index < (uint)configs.Length && configs[index] == null)
                {
                    configs[index] = config;
                }
            }

            for (int i = 0; i < configs.Length; i++)
            {
                AudioGroupConfig config = configs[i];
                if (config == null)
                {
                    configs[i] = CreateDefaultConfigForEditor((AudioType)i);
                    continue;
                }

                config.AudioType = (AudioType)i;
            }

            m_AudioGroupConfigs = configs;
        }

        internal static AudioGroupConfig[] CreateDefaultConfigsForEditor()
        {
            AudioGroupConfig[] configs = new AudioGroupConfig[(int)AudioType.Max];
            for (int i = 0; i < configs.Length; i++)
            {
                configs[i] = CreateDefaultConfigForEditor((AudioType)i);
            }

            return configs;
        }

        private static AudioGroupConfig CreateDefaultConfigForEditor(AudioType type)
        {
            AudioGroupConfig config = new AudioGroupConfig();
            switch (type)
            {
                case AudioType.Sound:
                    config.SetDefaults(type, "Sound", "SoundVolume", 24, 1f, false);
                    break;
                case AudioType.UISound:
                    config.SetDefaults(type, "UISound", "UISoundVolume", 12, 0f, false);
                    break;
                case AudioType.Music:
                    config.SetDefaults(type, "Music", "MusicVolume", 2, 0f, false);
                    break;
                case AudioType.Voice:
                    config.SetDefaults(type, "Voice", "VoiceVolume", 6, 1f, true);
                    break;
                case AudioType.Ambient:
                    config.SetDefaults(type, "Ambient", "AmbientVolume", 6, 1f, true);
                    break;
                default:
                    config.SetDefaults(AudioType.Sound, "Sound", "SoundVolume", 24, 1f, false);
                    break;
            }

            return config;
        }
#endif
    }
}
