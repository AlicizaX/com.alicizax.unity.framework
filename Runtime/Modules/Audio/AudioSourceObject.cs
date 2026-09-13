using AlicizaX;
using AlicizaX.ObjectPool;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioSourceObject : ObjectBase<AudioSource>
    {
        private AudioLowPassFilter _lowPassFilter;
        private GameObject _gameObject;

        public AudioSource Source => Target;
        public AudioLowPassFilter LowPassFilter => _lowPassFilter;

        internal void AttachLowPassFilter()
        {
            _lowPassFilter = Source.gameObject.AddComponent<AudioLowPassFilter>();
            _lowPassFilter.enabled = false;
            _lowPassFilter.cutoffFrequency = 22000f;
        }

        public static AudioSourceObject Create(string name, AudioSource source, AudioLowPassFilter lowPassFilter)
        {
            if (source == null)
            {
                throw new GameFrameworkException("Audio source is invalid.");
            }

            AudioSourceObject audioSourceObject = MemoryPool.Acquire<AudioSourceObject>();
            audioSourceObject.Initialize(name, source);
            audioSourceObject._gameObject = source.gameObject;
            audioSourceObject._lowPassFilter = lowPassFilter;
            return audioSourceObject;
        }

        protected internal override void OnSpawn()
        {
            if (_gameObject != null)
            {
                _gameObject.SetActive(true);
            }
        }

        protected internal override void OnUnspawn()
        {
            ResetSource();
            if (_gameObject != null)
            {
                _gameObject.SetActive(false);
            }
        }

        protected internal override void Release(bool isShutdown)
        {
            if (_gameObject != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_gameObject);
                }
                else
                {
                    Object.DestroyImmediate(_gameObject);
                }
            }
        }

        public override void Clear()
        {
            base.Clear();
            _lowPassFilter = null;
            _gameObject = null;
        }

        private void ResetSource()
        {
            if (Source == null)
            {
                return;
            }

            Source.Stop();
            Source.clip = null;
            Source.loop = false;
            Source.volume = 1f;
            Source.pitch = 1f;
            Source.spatialBlend = 0f;

            if (_lowPassFilter != null)
            {
                _lowPassFilter.enabled = false;
                _lowPassFilter.cutoffFrequency = 22000f;
            }
        }
    }
}
