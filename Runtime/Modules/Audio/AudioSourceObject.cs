using AlicizaX;
using AlicizaX.ObjectPool;
using UnityEngine;

namespace AlicizaX.Audio.Runtime
{
    internal sealed class AudioSourceObject : ObjectBase<AudioSource>
    {
        private AudioSource _source;
        private AudioLowPassFilter _lowPassFilter;

        public AudioSource Source => _source;
        public AudioLowPassFilter LowPassFilter => _lowPassFilter;

        internal AudioLowPassFilter EnsureLowPassFilter()
        {
            if (_lowPassFilter != null)
            {
                return _lowPassFilter;
            }

            if (_source == null)
            {
                return null;
            }

            if (!_source.TryGetComponent(out _lowPassFilter))
            {
                _lowPassFilter = _source.gameObject.AddComponent<AudioLowPassFilter>();
            }

            _lowPassFilter.enabled = false;
            _lowPassFilter.cutoffFrequency = 22000f;
            return _lowPassFilter;
        }

        public static AudioSourceObject Create(string name, AudioSource source, AudioLowPassFilter lowPassFilter)
        {
            if (source == null)
            {
                throw new GameFrameworkException("Audio source is invalid.");
            }

            AudioSourceObject audioSourceObject = MemoryPool.Acquire<AudioSourceObject>();
            audioSourceObject.Initialize(name, source);
            audioSourceObject._source = source;
            audioSourceObject._lowPassFilter = lowPassFilter;
            return audioSourceObject;
        }

        protected internal override void OnSpawn()
        {
            if (_source != null)
            {
                _source.gameObject.SetActive(true);
            }
        }

        protected internal override void OnUnspawn()
        {
            ResetSource();
            if (_source != null)
            {
                _source.gameObject.SetActive(false);
            }
        }

        protected internal override void Release(bool isShutdown)
        {
            if (_source != null)
            {
                Object.Destroy(_source.gameObject);
            }
        }

        public override void Clear()
        {
            base.Clear();
            _source = null;
            _lowPassFilter = null;
        }

        private void ResetSource()
        {
            if (_source == null)
            {
                return;
            }

            _source.Stop();
            _source.clip = null;
            _source.loop = false;
            _source.volume = 1f;
            _source.pitch = 1f;
            _source.spatialBlend = 0f;

            if (_lowPassFilter != null)
            {
                _lowPassFilter.enabled = false;
                _lowPassFilter.cutoffFrequency = 22000f;
            }
        }
    }
}
